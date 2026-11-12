using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;
using PayrollEngine.Mcp.Tools;
using PayrollEngine.Mcp.Tools.RegulatorTools;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Unit tests of the host startup rules: optional backend, Regulator roots,
/// tool registration of an offline deployment and get_server_info without backend.
/// No backend and no database required.</summary>
public class HostStartupTest(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{

    #region Backend requirement

    [Fact]
    public void RequiresBackend_DefaultPermissions_True()
    {
        // the default of every backend role is Read
        Assert.True(HostStartup.RequiresBackend(new McpPermissions(), new WorkspaceContext()));
    }

    [Theory]
    [InlineData("HR")]
    [InlineData("Payroll")]
    [InlineData("Report")]
    [InlineData("System")]
    public void RequiresBackend_SingleBackendRole_True(string role)
    {
        var permissions = role switch
        {
            "HR" => RegulatorOnly(McpPermission.Read, hr: McpPermission.Read),
            "Payroll" => RegulatorOnly(McpPermission.Read, payroll: McpPermission.Read),
            "Report" => RegulatorOnly(McpPermission.Read, report: McpPermission.Read),
            _ => RegulatorOnly(McpPermission.Read, system: McpPermission.Read)
        };
        Assert.True(HostStartup.RequiresBackend(permissions, new WorkspaceContext()));
    }

    [Theory]
    [InlineData(McpPermission.Read)]
    [InlineData(McpPermission.Write)]
    public void RequiresBackend_RegulatorOnly_NoBackendAccess_False(McpPermission regulator)
    {
        Assert.False(HostStartup.RequiresBackend(RegulatorOnly(regulator), new WorkspaceContext()));
    }

    [Theory]
    [InlineData(McpPermission.Read)]
    [InlineData(McpPermission.Write)]
    public void RequiresBackend_RegulatorBackendAccess_True(McpPermission backend)
    {
        var workspace = new WorkspaceContext { Backend = backend };
        Assert.True(HostStartup.RequiresBackend(RegulatorOnly(McpPermission.Read), workspace));
    }

    [Fact]
    public void RequiresBackend_NothingGranted_False()
    {
        Assert.False(HostStartup.RequiresBackend(RegulatorOnly(McpPermission.None), new WorkspaceContext()));
    }

    [Fact]
    public void DescribeBackendRequirement_NamesTheCauses()
    {
        var text = HostStartup.DescribeBackendRequirement(
            RegulatorOnly(McpPermission.Read, hr: McpPermission.Read),
            new WorkspaceContext { Backend = McpPermission.Read });
        Assert.Contains("Permissions:HR", text);
        Assert.Contains("McpServer:Regulator:Backend", text);
        Assert.DoesNotContain("Permissions:Payroll", text);
    }

    #endregion

    #region Isolation context from configuration

    [Fact]
    public void CreateIsolationContext_ReadsWorkspaceFromConfiguration()
    {
        var context = HostStartup.CreateIsolationContext(Configure(OfflineRegulator(fixture.Root)));

        Assert.Equal(McpPermission.Read, context.Permissions.Regulator);
        Assert.Single(context.Workspace.Roots);
        Assert.Equal(WorkspaceFixture.RepoMask, context.Workspace.RepoMask);
        Assert.False(HostStartup.RequiresBackend(context.Permissions, context.Workspace));
    }

    [Fact]
    public void CreateIsolationContext_RegulatorWithoutRoots_Throws()
    {
        var settings = OfflineRegulator(fixture.Root);
        settings.Remove("McpServer:Regulator:Roots:0");
        settings.Remove("McpServer:Regulator:RepoMask");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostStartup.CreateIsolationContext(Configure(settings)));
        Assert.Contains("Roots", exception.Message);
        Assert.Contains("Regulator", exception.Message);
    }

    [Fact]
    public void CreateIsolationContext_RegulatorWithoutRegulatorSection_Throws()
    {
        var settings = new Dictionary<string, string> { ["McpServer:Permissions:Regulator"] = "Write" };
        Assert.Throws<InvalidOperationException>(() => HostStartup.CreateIsolationContext(Configure(settings)));
    }

    [Fact]
    public void CreateIsolationContext_RegulatorNone_NeedsNoRoots()
    {
        // existing deployments have no Regulator configuration
        var context = HostStartup.CreateIsolationContext(Configure(new Dictionary<string, string>()));
        Assert.Equal(McpPermission.None, context.Permissions.Regulator);
        Assert.False(context.Workspace.IsConfigured);
    }

    [Fact]
    public void CreateIsolationContext_MissingRootFolder_Throws()
    {
        var settings = OfflineRegulator(fixture.Combine("missing"));
        Assert.Throws<InvalidOperationException>(() => HostStartup.CreateIsolationContext(Configure(settings)));
    }

    [Fact]
    public void CreateIsolationContext_InvalidBackendValue_Throws()
    {
        var settings = OfflineRegulator(fixture.Root);
        settings["McpServer:Regulator:Backend"] = "Maybe";
        Assert.Throws<InvalidOperationException>(() => HostStartup.CreateIsolationContext(Configure(settings)));
    }

    [Fact]
    public void CreateIsolationContext_BackendWithoutRoots_Throws()
    {
        var settings = new Dictionary<string, string>
        {
            ["McpServer:Permissions:Regulator"] = "None",
            ["McpServer:Regulator:Backend"] = "Read"
        };
        Assert.Throws<InvalidOperationException>(() => HostStartup.CreateIsolationContext(Configure(settings)));
    }

    #endregion

    #region Tool registration without backend

    [Fact]
    public void OfflineRegulator_RegistersServerInfoAndRegulatorTools_ThroughAssemblyScan()
    {
        var context = HostStartup.CreateIsolationContext(Configure(OfflineRegulator(fixture.Root)));

        var permitted = ToolRegistrar.GetPermittedTypes(
            typeof(ToolsMarker).Assembly, context.Permissions, context.Level).ToList();

        Assert.Contains(typeof(ServerInfoTools), permitted);
        Assert.Contains(typeof(LexQueryTools), permitted);
        Assert.Equal(2, permitted.Count);
        HostStartup.EnsureBackendIndependentTools(permitted, backendConnected: false);
    }

    [Fact]
    public void EnsureBackendIndependentTools_BackendToolWithoutBackend_Throws()
    {
        var permitted = ToolRegistrar.GetPermittedTypes(
            typeof(ToolsMarker).Assembly, new McpPermissions(), IsolationLevel.MultiTenant).ToList();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostStartup.EnsureBackendIndependentTools(permitted, backendConnected: false));
        Assert.Contains("backend", exception.Message);
        HostStartup.EnsureBackendIndependentTools(permitted, backendConnected: true);
    }

    #endregion

    #region get_server_info

    [Fact]
    public async Task ServerInfo_WithoutBackend_ReportsOfflineAndRegulatorMode()
    {
        var context = HostStartup.CreateIsolationContext(Configure(OfflineRegulator(fixture.Root)));

        // the tool is created like the MCP server does: from the service container, no backend client registered
        var services = new ServiceCollection().AddSingleton(context).BuildServiceProvider();
        var tool = ActivatorUtilities.CreateInstance<ServerInfoTools>(services);

        using var document = JsonDocument.Parse(await tool.GetServerInfoAsync());
        var root = document.RootElement;

        Assert.Equal("offline", root.GetProperty("backend").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("lexVersion").GetString()));
        var regulator = root.GetProperty("regulator");
        Assert.Equal("Read", regulator.GetProperty("mode").GetString());
        Assert.Equal("None", regulator.GetProperty("backend").GetString());
        Assert.Equal(1, regulator.GetProperty("rootCount").GetInt32());
        Assert.Equal(WorkspaceFixture.RepoMask, regulator.GetProperty("repoMask").GetString());
        Assert.Equal("Read", root.GetProperty("permissions").GetProperty("Regulator").GetString());
        Assert.Equal("None", root.GetProperty("permissions").GetProperty("HR").GetString());
    }

    [Fact]
    public async Task ServerInfo_DoesNotDiscloseRootPaths()
    {
        var context = HostStartup.CreateIsolationContext(Configure(OfflineRegulator(fixture.Root)));
        var tool = new ServerInfoTools(context);

        var json = await tool.GetServerInfoAsync();
        Assert.DoesNotContain(fixture.Root, json, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Helpers

    private static IConfiguration Configure(Dictionary<string, string> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    /// <summary>Regulator-only deployment: backend roles None, Regulator Read, offline</summary>
    private static Dictionary<string, string> OfflineRegulator(string root) => new()
    {
        ["McpServer:Permissions:HR"] = "None",
        ["McpServer:Permissions:Payroll"] = "None",
        ["McpServer:Permissions:Report"] = "None",
        ["McpServer:Permissions:System"] = "None",
        ["McpServer:Permissions:Regulator"] = "Read",
        ["McpServer:Regulator:Roots:0"] = root,
        ["McpServer:Regulator:RepoMask"] = WorkspaceFixture.RepoMask
    };

    private static McpPermissions RegulatorOnly(McpPermission regulator,
        McpPermission hr = McpPermission.None, McpPermission payroll = McpPermission.None,
        McpPermission report = McpPermission.None, McpPermission system = McpPermission.None) => new()
    {
        HR = hr,
        Payroll = payroll,
        Report = report,
        System = system,
        Regulator = regulator
    };

    #endregion
}
