using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using ModelContextProtocol.Server;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;
using PayrollEngine.Mcp.Tools;
using PayrollEngine.Mcp.Tools.PayrollTools;
using PayrollEngine.Mcp.Tools.PeopleTools;
using PayrollEngine.Mcp.Tools.RegulatorTools;
using PayrollEngine.Mcp.Tools.ReportTools;
using PayrollEngine.Mcp.Tools.TenantTools;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Unit tests for ToolRegistrar permission filtering.
/// Verifies that tool classes are registered or excluded based on McpPermissions —
/// without requiring a running backend.</summary>
public class ToolRegistrarTest
{
    private static readonly Assembly ToolsAssembly = typeof(ToolsMarker).Assembly;

    // HR tool classes
    private static readonly System.Type[] HrTools =
    [
        typeof(DivisionQueryTools),
        typeof(EmployeeQueryTools),
        typeof(CaseValueQueryTools),
        typeof(CaseChangeQueryTools),
        typeof(CaseSchemaQueryTools)
    ];

    // Payroll tool classes
    private static readonly System.Type[] PayrollTools =
    [
        typeof(PayrollQueryTools),
        typeof(PayrollResultTools),
        typeof(ConsolidatedResultTools),
        typeof(PayrollPreviewTools),
        typeof(PayrollStructureTools),
        typeof(LookupQueryTools)
    ];

    // Report tool classes
    private static readonly System.Type[] ReportTools =
    [
        typeof(ReportQueryTools)
    ];

    // System tool classes
    private static readonly System.Type[] SystemTools =
    [
        typeof(TenantQueryTools),
        typeof(UserQueryTools)
    ];

    #region All roles Read @ MultiTenant — full registration

    [Fact]
    public void AllRead_RegistersAllHrTools()
    {
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.All(HrTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void AllRead_RegistersAllPayrollTools()
    {
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void AllRead_RegistersAllReportTools()
    {
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.All(ReportTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void AllRead_RegistersAllSystemTools()
    {
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    #endregion

    #region All roles None — only ServerInfo registered

    [Fact]
    public void AllNone_ExcludesAllHrTools()
    {
        var permitted = GetPermitted(AllNone()).ToList();
        Assert.All(HrTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void AllNone_ExcludesAllPayrollTools()
    {
        var permitted = GetPermitted(AllNone()).ToList();
        Assert.All(PayrollTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void AllNone_ExcludesAllReportTools()
    {
        var permitted = GetPermitted(AllNone()).ToList();
        Assert.All(ReportTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void AllNone_ExcludesAllSystemTools()
    {
        var permitted = GetPermitted(AllNone()).ToList();
        Assert.All(SystemTools, t => Assert.DoesNotContain(t, permitted));
    }

    #endregion

    #region Single role None — only that role excluded

    [Fact]
    public void HrNone_ExcludesHrTools_KeepsOthers()
    {
        var permissions = new McpPermissions { HR = McpPermission.None };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void PayrollNone_ExcludesPayrollTools_KeepsOthers()
    {
        var permissions = new McpPermissions { Payroll = McpPermission.None };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void ReportNone_ExcludesReportTools_KeepsOthers()
    {
        var permissions = new McpPermissions { Report = McpPermission.None };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
        Assert.All(ReportTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void SystemNone_ExcludesSystemTools_KeepsOthers()
    {
        var permissions = new McpPermissions { System = McpPermission.None };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
        Assert.All(ReportTools, t => Assert.Contains(t, permitted));
        Assert.All(SystemTools, t => Assert.DoesNotContain(t, permitted));
    }

    #endregion

    #region Persona configurations

    [Fact]
    public void HrManagerPersona_OnlyHrToolsRegistered()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.Read,
            Payroll = McpPermission.None,
            System = McpPermission.None
        };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(SystemTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void PayrollSpecialistPersona_HrAndPayrollRegistered()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.Read,
            Payroll = McpPermission.Read,
            System = McpPermission.None
        };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
        Assert.All(SystemTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void SystemAdminPersona_OnlySystemToolsRegistered()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.None,
            Payroll = McpPermission.None,
            System = McpPermission.Read
        };
        var permitted = GetPermitted(permissions).ToList();

        Assert.All(HrTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(PayrollTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    #endregion

    #region IsolationLevel filtering

    [Fact]
    public void Division_ExcludesSystemAndReportTools()
    {
        var permitted = GetPermitted(AllRead(), IsolationLevel.Division).ToList();
        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
        Assert.All(ReportTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(SystemTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void Employee_ExcludesPayrollSystemAndReportTools()
    {
        var permitted = GetPermitted(AllRead(), IsolationLevel.Employee).ToList();
        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(ReportTools, t => Assert.DoesNotContain(t, permitted));
        Assert.All(SystemTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void Tenant_AllRolesApplicable()
    {
        var permitted = GetPermitted(AllRead(), IsolationLevel.Tenant).ToList();
        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(PayrollTools, t => Assert.Contains(t, permitted));
        Assert.All(ReportTools, t => Assert.Contains(t, permitted));
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    #endregion

    #region Tool count — detects unregistered new tool classes

    [Fact]
    public void AllRead_TotalToolCount_Is15()
    {
        // ServerInfo(1, no role) + HR(5) + Payroll(6) + Report(1) + System(2) = 15
        // Update this count whenever a new tool class is added.
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.Equal(15, permitted.Count);
    }

    [Fact]
    public void AllNone_ServerInfoStillRegistered()
    {
        // ServerInfoTools has no [ToolRole] — always registered regardless of permissions
        var permitted = GetPermitted(AllNone()).ToList();
        Assert.Contains(typeof(ServerInfoTools), permitted);
    }

    [Fact]
    public void AllNone_OnlyServerInfoRegistered()
    {
        // With all roles None, only the role-less ServerInfoTools should be registered
        var permitted = GetPermitted(AllNone()).ToList();
        Assert.Single(permitted);
    }

    [Fact]
    public void HrToolCount_Is5()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.Read,
            Payroll = McpPermission.None,
            Report = McpPermission.None,
            System = McpPermission.None
        };
        var permitted = GetPermitted(permissions).ToList();
        Assert.Equal(6, permitted.Count); // HR(5) + ServerInfo(1)
    }

    [Fact]
    public void PayrollToolCount_Is6()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.None,
            Payroll = McpPermission.Read,
            Report = McpPermission.None,
            System = McpPermission.None
        };
        var permitted = GetPermitted(permissions).ToList();
        Assert.Equal(7, permitted.Count); // Payroll(6) + ServerInfo(1)
    }

    [Fact]
    public void ReportToolCount_Is1()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.None,
            Payroll = McpPermission.None,
            Report = McpPermission.Read,
            System = McpPermission.None
        };
        var permitted = GetPermitted(permissions).ToList();
        Assert.Equal(2, permitted.Count); // Report(1) + ServerInfo(1)
    }

    [Fact]
    public void SystemToolCount_Is2()
    {
        var permissions = new McpPermissions
        {
            HR = McpPermission.None,
            Payroll = McpPermission.None,
            Report = McpPermission.None,
            System = McpPermission.Read
        };
        var permitted = GetPermitted(permissions).ToList();
        Assert.Equal(3, permitted.Count); // System(2) + ServerInfo(1)
    }

    #endregion

    #region Tool base hierarchy

    [Fact]
    public void ToolBase_DerivesFromIsolationToolBase()
    {
        Assert.True(typeof(ToolBase).IsSubclassOf(typeof(IsolationToolBase)));
        Assert.True(typeof(ToolBase).IsAbstract);
        Assert.True(typeof(IsolationToolBase).IsAbstract);
    }

    [Fact]
    public void AllRead_AllToolClasses_DeriveFromIsolationToolBase()
    {
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.All(permitted, t => Assert.True(t.IsSubclassOf(typeof(IsolationToolBase)), t.Name));
    }

    #endregion

    #region Regulator

    private static readonly Assembly ProbeAssembly = typeof(RegulatorProbeTool).Assembly;

    private static bool ProbeRegistered(McpPermissions permissions, IsolationLevel level) =>
        ToolRegistrar.GetPermittedTypes(ProbeAssembly, permissions, level).Contains(typeof(RegulatorProbeTool));

    [Fact]
    public void Regulator_DefaultPermission_IsNone()
    {
        Assert.Equal(McpPermission.None, new McpPermissions().Regulator);
        Assert.False(new McpPermissions().IsGranted(McpRole.Regulator, McpPermission.Read));
    }

    [Fact]
    public void Regulator_DefaultPermissions_DoNotRegisterRegulatorTools()
    {
        Assert.False(ProbeRegistered(new McpPermissions(), IsolationLevel.MultiTenant));
    }

    [Theory]
    [InlineData(IsolationLevel.MultiTenant, true)]
    [InlineData(IsolationLevel.Tenant, true)]
    [InlineData(IsolationLevel.Division, false)]
    [InlineData(IsolationLevel.Employee, false)]
    public void Regulator_Read_FollowsIsolationMatrix(IsolationLevel level, bool expected)
    {
        var permissions = new McpPermissions { Regulator = McpPermission.Read };
        Assert.Equal(expected, ProbeRegistered(permissions, level));
    }

    [Fact]
    public void Regulator_Granted_DoesNotAffectOtherRoles()
    {
        var permissions = new McpPermissions { Regulator = McpPermission.Read };
        var permitted = GetPermitted(permissions).ToList();
        Assert.All(HrTools, t => Assert.Contains(t, permitted));
        Assert.All(SystemTools, t => Assert.Contains(t, permitted));
    }

    [Fact]
    public void Regulator_Configuration_WithoutSection_DefaultsToNone()
    {
        var permissions = McpPermissions.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal(McpPermission.Read, permissions.HR);
        Assert.Equal(McpPermission.None, permissions.Regulator);
    }

    [Fact]
    public void Regulator_Configuration_SectionWithoutRegulator_DefaultsToNone()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["McpServer:Permissions:HR"] = "Read" })
            .Build();
        Assert.Equal(McpPermission.None, McpPermissions.FromConfiguration(configuration).Regulator);
    }

    [Fact]
    public void Regulator_Configuration_ReadsRegulatorPermission()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["McpServer:Permissions:Regulator"] = "Read" })
            .Build();
        Assert.Equal(McpPermission.Read, McpPermissions.FromConfiguration(configuration).Regulator);
    }

    [McpServerToolType]
    [ToolRole(McpRole.Regulator)]
    private sealed class RegulatorProbeTool;

    // real Regulator tool classes of the Mcp.Tools assembly
    private static readonly System.Type[] RegulatorTools =
    [
        typeof(LexQueryTools)
    ];

    [Fact]
    public void RegulatorTools_DefaultPermissions_AreInvisible()
    {
        var permitted = GetPermitted(new McpPermissions()).ToList();
        Assert.All(RegulatorTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void RegulatorTools_AllOtherRolesRead_AreInvisible()
    {
        // AllRead() leaves the Regulator permission at its default None
        var permitted = GetPermitted(AllRead()).ToList();
        Assert.All(RegulatorTools, t => Assert.DoesNotContain(t, permitted));
    }

    [Fact]
    public void RegulatorTools_ExplicitNone_AreInvisible()
    {
        var permissions = new McpPermissions { Regulator = McpPermission.None };
        Assert.All(RegulatorTools, t => Assert.DoesNotContain(t, GetPermitted(permissions).ToList()));
    }

    [Theory]
    [InlineData(McpPermission.Read)]
    [InlineData(McpPermission.Write)]
    public void RegulatorTools_Granted_AreRegistered(McpPermission regulator)
    {
        var permissions = new McpPermissions { Regulator = regulator };
        var permitted = GetPermitted(permissions).ToList();
        Assert.All(RegulatorTools, t => Assert.Contains(t, permitted));
    }

    [Theory]
    [InlineData(IsolationLevel.MultiTenant, true)]
    [InlineData(IsolationLevel.Tenant, true)]
    [InlineData(IsolationLevel.Division, false)]
    [InlineData(IsolationLevel.Employee, false)]
    public void RegulatorTools_FollowTheIsolationMatrix(IsolationLevel level, bool expected)
    {
        var permissions = new McpPermissions { Regulator = McpPermission.Read };
        var permitted = GetPermitted(permissions, level).ToList();
        Assert.All(RegulatorTools, t => Assert.Equal(expected, permitted.Contains(t)));
    }

    [Fact]
    public void RegulatorTools_OnlyRegulatorGranted_RegistersServerInfoAndRegulatorTools()
    {
        var permitted = GetPermitted(new McpPermissions
        {
            HR = McpPermission.None,
            Payroll = McpPermission.None,
            Report = McpPermission.None,
            System = McpPermission.None,
            Regulator = McpPermission.Read
        }).ToList();
        Assert.Equal(1 + RegulatorTools.Length, permitted.Count);
        Assert.Contains(typeof(ServerInfoTools), permitted);
    }

    [Fact]
    public void RegulatorTools_NeedNoBackendClient()
    {
        Assert.All(RegulatorTools, t =>
        {
            Assert.True(t.IsSubclassOf(typeof(IsolationToolBase)), t.Name);
            Assert.False(t.IsSubclassOf(typeof(ToolBase)), t.Name);
            Assert.All(t.GetConstructors(), c => Assert.DoesNotContain(c.GetParameters(),
                p => p.ParameterType.Name == "PayrollHttpClient"));
        });
    }

    #endregion

    #region Helpers

    // Default isolation level for permission-only tests: MultiTenant (all roles applicable)
    private static IEnumerable<System.Type> GetPermitted(
        McpPermissions permissions,
        IsolationLevel level = IsolationLevel.MultiTenant) =>
        ToolRegistrar.GetPermittedTypes(ToolsAssembly, permissions, level);

    private static McpPermissions AllRead() => new()
    {
        HR = McpPermission.Read,
        Payroll = McpPermission.Read,
        Report = McpPermission.Read,
        System = McpPermission.Read
    };

    private static McpPermissions AllNone() => new()
    {
        HR = McpPermission.None,
        Payroll = McpPermission.None,
        Report = McpPermission.None,
        System = McpPermission.None
    };

    #endregion
}
