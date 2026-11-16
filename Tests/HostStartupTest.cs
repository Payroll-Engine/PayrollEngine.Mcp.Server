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

    [Theory]
    // ReSharper disable once StringLiteralTypo
    [InlineData("Tennant")]
    [InlineData("Multi Tenant")]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("Tenant,Division")]
    [InlineData("MultiTenant, Employee")]
    public void CreateIsolationContext_InvalidIsolationLevel_ThrowsWithKeyValueAndAllowedValues(string value)
    {
        var settings = OfflineRegulator(fixture.Root);
        settings["McpServer:IsolationLevel"] = value;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostStartup.CreateIsolationContext(Configure(settings)));

        Assert.Contains("McpServer:IsolationLevel", exception.Message);
        Assert.Contains($"'{value}'", exception.Message);
        foreach (var name in Enum.GetNames<IsolationLevel>())
        {
            Assert.Contains(name, exception.Message);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateIsolationContext_MissingOrEmptyIsolationLevel_IsMultiTenant(string value)
    {
        var settings = OfflineRegulator(fixture.Root);
        if (value != null)
        {
            settings["McpServer:IsolationLevel"] = value;
        }

        Assert.Equal(IsolationLevel.MultiTenant,
            HostStartup.CreateIsolationContext(Configure(settings)).Level);
    }

    [Theory]
    [InlineData("MultiTenant", IsolationLevel.MultiTenant)]
    [InlineData("multitenant", IsolationLevel.MultiTenant)]
    [InlineData("TENANT", IsolationLevel.Tenant)]
    [InlineData("Division", IsolationLevel.Division)]
    [InlineData("employee", IsolationLevel.Employee)]
    public void ParseIsolationLevel_ValidNames_IgnoreCase(string value, IsolationLevel expected)
    {
        Assert.Equal(expected, HostStartup.ParseIsolationLevel(value));
    }

    [Fact]
    public void CreateIsolationContext_ValidIsolationLevel_NeedsItsIdentifiers()
    {
        var settings = OfflineRegulator(fixture.Root);
        settings["McpServer:IsolationLevel"] = "tenant";
        settings["McpServer:TenantIdentifier"] = "acme";

        var context = HostStartup.CreateIsolationContext(Configure(settings));

        Assert.Equal(IsolationLevel.Tenant, context.Level);
    }

    [Theory]
    [InlineData("HR")]
    [InlineData("Payroll")]
    [InlineData("Report")]
    [InlineData("System")]
    [InlineData("Regulator")]
    public void CreateIsolationContext_InvalidPermission_ThrowsWithKeyValueAndAllowedValues(string role)
    {
        var settings = OfflineRegulator(fixture.Root);
        // ReSharper disable once StringLiteralTypo
        settings[$"McpServer:Permissions:{role}"] = "Wirte";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostStartup.CreateIsolationContext(Configure(settings)));

        Assert.Contains($"McpServer:Permissions:{role}", exception.Message);
        // ReSharper disable once StringLiteralTypo
        Assert.Contains("'Wirte'", exception.Message);
        foreach (var name in Enum.GetNames<McpPermission>())
        {
            Assert.Contains(name, exception.Message);
        }
    }

    [Theory]
    [InlineData("9")]
    [InlineData("None,Write")]
    [InlineData("Read, Write")]
    public void CreateIsolationContext_NumericOrCompositePermission_Throws(string value)
    {
        var settings = OfflineRegulator(fixture.Root);
        settings["McpServer:Permissions:Regulator"] = value;

        Assert.Throws<InvalidOperationException>(() =>
            HostStartup.CreateIsolationContext(Configure(settings)));
    }

    [Fact]
    public void CreateIsolationContext_PermissionNames_IgnoreCase()
    {
        var settings = OfflineRegulator(fixture.Root);
        settings["McpServer:Permissions:HR"] = "READ";
        settings["McpServer:Permissions:Regulator"] = "write";

        var permissions = HostStartup.CreateIsolationContext(Configure(settings)).Permissions;

        Assert.Equal(McpPermission.Read, permissions.HR);
        Assert.Equal(McpPermission.Write, permissions.Regulator);
    }

    [Fact]
    public void CreateIsolationContext_MissingPermissionKeyInSection_IsNone()
    {
        // a key that is absent from an existing section stays None
        var settings = new Dictionary<string, string> { ["McpServer:Permissions:Payroll"] = "Read" };

        var permissions = HostStartup.CreateIsolationContext(Configure(settings)).Permissions;

        Assert.Equal(McpPermission.Read, permissions.Payroll);
        Assert.Equal(McpPermission.None, permissions.HR);
        Assert.Equal(McpPermission.None, permissions.Report);
        Assert.Equal(McpPermission.None, permissions.System);
        Assert.Equal(McpPermission.None, permissions.Regulator);
    }

    [Fact]
    public void CreateIsolationContext_MissingPermissionSection_KeepsDefaults()
    {
        var permissions = HostStartup.CreateIsolationContext(Configure(new Dictionary<string, string>())).Permissions;

        Assert.Equal(McpPermission.Read, permissions.HR);
        Assert.Equal(McpPermission.Read, permissions.Payroll);
        Assert.Equal(McpPermission.Read, permissions.Report);
        Assert.Equal(McpPermission.Read, permissions.System);
        Assert.Equal(McpPermission.None, permissions.Regulator);
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
