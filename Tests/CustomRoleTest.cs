using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;
using PayrollEngine.Mcp.Tools;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Unit tests for the roles defined by a server (McpRoleDefinition):
/// definition rules, permissions from configuration and tool registration.</summary>
public class CustomRoleTest
{
    private const string AuditRole = "Audit";
    private const string OfflineRole = "Offline";

    private static readonly Assembly ProbeAssembly = typeof(AuditReadProbeTool).Assembly;

    private static readonly McpRoleDefinition Audit =
        new(AuditRole, [IsolationLevel.MultiTenant, IsolationLevel.Tenant]);

    private static readonly McpRoleDefinition Offline =
        new(OfflineRole, [IsolationLevel.MultiTenant], requiresBackend: false);

    #region Definition

    [Fact]
    public void Definition_KeepsNameLevelsAndBackend()
    {
        Assert.Equal(AuditRole, Audit.Name);
        Assert.Equal(new[] { IsolationLevel.MultiTenant, IsolationLevel.Tenant }, Audit.IsolationLevels.ToArray());
        Assert.True(Audit.RequiresBackend);
        Assert.False(Offline.RequiresBackend);
    }

    [Theory]
    [InlineData(IsolationLevel.MultiTenant, true)]
    [InlineData(IsolationLevel.Tenant, true)]
    [InlineData(IsolationLevel.Division, false)]
    [InlineData(IsolationLevel.Employee, false)]
    public void Definition_IsApplicable_FollowsItsLevels(IsolationLevel level, bool expected)
    {
        Assert.Equal(expected, Audit.IsApplicable(level));
    }

    [Fact]
    public void Definition_DuplicateLevels_AreMerged()
    {
        var role = new McpRoleDefinition(AuditRole, [IsolationLevel.Tenant, IsolationLevel.Tenant]);
        Assert.Single(role.IsolationLevels);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1Audit")]
    [InlineData("Audit:Read")]
    [InlineData("Audit Role")]
    [InlineData("Audit-Role")]
    public void Definition_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new McpRoleDefinition(name, [IsolationLevel.Tenant]));
    }

    [Theory]
    [InlineData("HR")]
    [InlineData("payroll")]
    [InlineData("Regulator")]
    public void Definition_BuiltInName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new McpRoleDefinition(name, [IsolationLevel.Tenant]));
    }

    [Fact]
    public void Definition_WithoutLevels_Throws()
    {
        Assert.Throws<ArgumentException>(() => new McpRoleDefinition(AuditRole, []));
        Assert.Throws<ArgumentException>(() => new McpRoleDefinition(AuditRole, null));
    }

    [Theory]
    [InlineData("HR", true)]
    [InlineData("system", true)]
    [InlineData(AuditRole, false)]
    [InlineData(null, false)]
    public void Definition_IsBuiltInName(string name, bool expected)
    {
        Assert.Equal(expected, McpRoleDefinition.IsBuiltInName(name));
    }

    #endregion

    #region Attribute

    [Fact]
    public void Attribute_BuiltInRole_SetsRoleName()
    {
        var attribute = new ToolRoleAttribute(McpRole.Payroll, McpPermission.Write);
        Assert.True(attribute.IsBuiltInRole);
        Assert.Equal(McpRole.Payroll, attribute.Role);
        Assert.Equal("Payroll", attribute.RoleName);
    }

    [Fact]
    public void Attribute_CustomRole_IsNotBuiltIn()
    {
        var attribute = new ToolRoleAttribute(AuditRole, McpPermission.Write);
        Assert.False(attribute.IsBuiltInRole);
        Assert.Equal(AuditRole, attribute.RoleName);
        Assert.Equal(McpPermission.Write, attribute.Required);
        Assert.Null(attribute.RequiredIsolationLevel);
    }

    [Fact]
    public void Attribute_BuiltInName_SelectsTheBuiltInRole()
    {
        var attribute = new ToolRoleAttribute("report");
        Assert.True(attribute.IsBuiltInRole);
        Assert.Equal(McpRole.Report, attribute.Role);
        Assert.Equal("Report", attribute.RoleName);
    }

    [Fact]
    public void Attribute_CustomRole_WithIsolationLevel()
    {
        var attribute = new ToolRoleAttribute(AuditRole, McpPermission.Read, IsolationLevel.Tenant);
        Assert.Equal(IsolationLevel.Tenant, attribute.RequiredIsolationLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Attribute_EmptyName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new ToolRoleAttribute(name));
    }

    #endregion

    #region Permissions

    [Fact]
    public void Permissions_Default_CustomRoleIsNone()
    {
        var permissions = new McpPermissions();
        Assert.Empty(permissions.CustomRoles);
        Assert.Equal(McpPermission.None, permissions.GetPermission(AuditRole));
        Assert.False(permissions.IsGranted(AuditRole, McpPermission.Read));
    }

    [Fact]
    public void Permissions_BuiltInRoleByName()
    {
        var permissions = new McpPermissions { HR = McpPermission.Write, Regulator = McpPermission.Read };
        Assert.Equal(McpPermission.Write, permissions.GetPermission("hr"));
        Assert.True(permissions.IsGranted("Regulator", McpPermission.Read));
        Assert.False(permissions.IsGranted("Regulator", McpPermission.Write));
    }

    [Fact]
    public void Permissions_CustomRoleByName_IgnoresCase()
    {
        var permissions = new McpPermissions
        {
            CustomRoles = new Dictionary<string, McpPermission> { [AuditRole] = McpPermission.Write }
        };
        Assert.Equal(McpPermission.Write, permissions.GetPermission("audit"));
        Assert.True(permissions.IsGranted("AUDIT", McpPermission.Write));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Permissions_EmptyRoleName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new McpPermissions().GetPermission(name));
    }

    [Fact]
    public void Configuration_WithoutSection_CustomRoleIsNone()
    {
        var permissions = McpPermissions.FromConfiguration(new ConfigurationBuilder().Build(), [Audit]);
        Assert.Equal(McpPermission.None, permissions.GetPermission(AuditRole));
        Assert.Equal(McpPermission.Read, permissions.HR);
    }

    [Fact]
    public void Configuration_ReadsCustomRole()
    {
        var permissions = McpPermissions.FromConfiguration(Configuration(new()
        {
            ["McpServer:Permissions:Audit"] = "Write",
            ["McpServer:Permissions:HR"] = "Read"
        }), [Audit, Offline]);

        Assert.Equal(McpPermission.Write, permissions.GetPermission(AuditRole));
        Assert.Equal(McpPermission.None, permissions.GetPermission(OfflineRole));
        Assert.Equal(McpPermission.Read, permissions.HR);
        Assert.Equal(McpPermission.None, permissions.Payroll);
        Assert.Equal(2, permissions.CustomRoles.Count);
    }

    [Fact]
    public void Configuration_UndefinedKey_IsIgnored()
    {
        var permissions = McpPermissions.FromConfiguration(Configuration(new()
        {
            ["McpServer:Permissions:Audit"] = "Write"
        }));

        Assert.Empty(permissions.CustomRoles);
        Assert.Equal(McpPermission.None, permissions.GetPermission(AuditRole));
    }

    [Fact]
    public void Configuration_InvalidCustomRoleValue_Throws()
    {
        var configuration = Configuration(new() { ["McpServer:Permissions:Audit"] = "Full" });
        var exception = Assert.Throws<InvalidOperationException>(() => McpPermissions.FromConfiguration(configuration, [Audit]));
        Assert.Contains("McpServer:Permissions:Audit", exception.Message);
    }

    [Fact]
    public void Configuration_DuplicateDefinition_Throws()
    {
        var duplicate = new McpRoleDefinition("audit", [IsolationLevel.Tenant]);
        Assert.Throws<ArgumentException>(() =>
            McpPermissions.FromConfiguration(new ConfigurationBuilder().Build(), [Audit, duplicate]));
    }

    #endregion

    #region Registration

    [Fact]
    public void Registration_WithoutDefinition_NotRegistered()
    {
        var permitted = GetPermitted(Granted(McpPermission.Write), IsolationLevel.MultiTenant, []);
        Assert.DoesNotContain(typeof(AuditReadProbeTool), permitted);
        Assert.DoesNotContain(typeof(AuditWriteProbeTool), permitted);
    }

    [Fact]
    public void Registration_DefinedButNotGranted_NotRegistered()
    {
        var permitted = GetPermitted(new McpPermissions(), IsolationLevel.MultiTenant, [Audit]);
        Assert.DoesNotContain(typeof(AuditReadProbeTool), permitted);
    }

    [Theory]
    [InlineData(McpPermission.Read, true, false)]
    [InlineData(McpPermission.Write, true, true)]
    public void Registration_FollowsPermission(McpPermission granted, bool read, bool write)
    {
        var permitted = GetPermitted(Granted(granted), IsolationLevel.MultiTenant, [Audit]);
        Assert.Equal(read, permitted.Contains(typeof(AuditReadProbeTool)));
        Assert.Equal(write, permitted.Contains(typeof(AuditWriteProbeTool)));
    }

    [Theory]
    [InlineData(IsolationLevel.MultiTenant, true)]
    [InlineData(IsolationLevel.Tenant, true)]
    [InlineData(IsolationLevel.Division, false)]
    [InlineData(IsolationLevel.Employee, false)]
    public void Registration_FollowsDefinedLevels(IsolationLevel level, bool expected)
    {
        var permitted = GetPermitted(Granted(McpPermission.Read), level, [Audit]);
        Assert.Equal(expected, permitted.Contains(typeof(AuditReadProbeTool)));
    }

    [Theory]
    [InlineData(IsolationLevel.MultiTenant, false)]
    [InlineData(IsolationLevel.Tenant, true)]
    public void Registration_RequiredIsolationLevel_OverridesDefinedLevels(IsolationLevel level, bool expected)
    {
        var permitted = GetPermitted(Granted(McpPermission.Read), level, [Audit]);
        Assert.Equal(expected, permitted.Contains(typeof(AuditTenantProbeTool)));
    }

    [Fact]
    public void Registration_BuiltInNameAttribute_FollowsBuiltInRole()
    {
        var permissions = new McpPermissions { Report = McpPermission.None };
        Assert.DoesNotContain(typeof(ReportByNameProbeTool), GetPermitted(permissions, IsolationLevel.MultiTenant, []));

        permissions = new McpPermissions { Report = McpPermission.Read };
        Assert.Contains(typeof(ReportByNameProbeTool), GetPermitted(permissions, IsolationLevel.MultiTenant, []));
        Assert.DoesNotContain(typeof(ReportByNameProbeTool), GetPermitted(permissions, IsolationLevel.Division, []));
    }

    [Fact]
    public void Registration_CustomRoles_DoNotChangeTheBuiltInTools()
    {
        var toolsAssembly = typeof(ToolsMarker).Assembly;
        var withoutRoles = ToolRegistrar.GetPermittedTypes(toolsAssembly, new McpPermissions(), IsolationLevel.MultiTenant).ToList();
        var withRoles = ToolRegistrar.GetPermittedTypes(toolsAssembly, Granted(McpPermission.Write), IsolationLevel.MultiTenant, [Audit, Offline]).ToList();
        Assert.Equal(withoutRoles, withRoles);
    }

    [Fact]
    public void Registration_DuplicateDefinition_Throws()
    {
        var duplicate = new McpRoleDefinition("AUDIT", [IsolationLevel.Tenant]);
        Assert.Throws<ArgumentException>(() =>
            ToolRegistrar.GetPermittedTypes(ProbeAssembly, new McpPermissions(), IsolationLevel.Tenant, [Audit, duplicate]));
    }

    #endregion

    #region Helpers

    private static List<Type> GetPermitted(McpPermissions permissions, IsolationLevel level, IEnumerable<McpRoleDefinition> roles) =>
        ToolRegistrar.GetPermittedTypes(ProbeAssembly, permissions, level, roles).ToList();

    private static McpPermissions Granted(McpPermission audit) => new()
    {
        CustomRoles = new Dictionary<string, McpPermission> { [AuditRole] = audit }
    };

    private static IConfiguration Configuration(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [McpServerToolType]
    [ToolRole(AuditRole)]
    private sealed class AuditReadProbeTool;

    [McpServerToolType]
    [ToolRole(AuditRole, McpPermission.Write)]
    private sealed class AuditWriteProbeTool;

    [McpServerToolType]
    [ToolRole(AuditRole, McpPermission.Read, IsolationLevel.Tenant)]
    private sealed class AuditTenantProbeTool;

    [McpServerToolType]
    [ToolRole("Report")]
    private sealed class ReportByNameProbeTool;

    #endregion
}
