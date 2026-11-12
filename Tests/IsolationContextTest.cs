using System;
using PayrollEngine.Mcp.Core.Isolation;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Unit tests for IsolationContext.Validate().
/// Verifies that required configuration keys are enforced per isolation level.</summary>
public class IsolationContextTest
{
    #region MultiTenant — always valid

    [Fact]
    public void MultiTenant_NoIdentifiers_IsValid()
    {
        var ctx = new IsolationContext { Level = IsolationLevel.MultiTenant };
        ctx.Validate(); // must not throw
    }

    #endregion

    #region Tenant

    [Fact]
    public void Tenant_WithIdentifier_IsValid()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Tenant,
            TenantIdentifier = "acme-corp"
        };
        ctx.Validate(); // must not throw
    }

    [Fact]
    public void Tenant_MissingIdentifier_Throws()
    {
        var ctx = new IsolationContext { Level = IsolationLevel.Tenant };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Tenant_WhitespaceIdentifier_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Tenant,
            TenantIdentifier = "   "
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    #endregion

    #region Division

    [Fact]
    public void Division_WithAllRequired_IsValid()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Division,
            TenantIdentifier = "acme-corp",
            DivisionName = "sales"
        };
        ctx.Validate(); // must not throw
    }

    [Fact]
    public void Division_MissingTenantIdentifier_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Division,
            DivisionName = "sales"
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Division_MissingDivisionName_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Division,
            TenantIdentifier = "acme-corp"
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Division_WhitespaceDivisionName_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Division,
            TenantIdentifier = "acme-corp",
            DivisionName = "   "
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    #endregion

    #region Employee

    [Fact]
    public void Employee_WithAllRequired_IsValid()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Employee,
            TenantIdentifier = "acme-corp",
            EmployeeIdentifier = "mario.nunez@acme.com"
        };
        ctx.Validate(); // must not throw
    }

    [Fact]
    public void Employee_MissingTenantIdentifier_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Employee,
            EmployeeIdentifier = "mario.nunez@acme.com"
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Employee_MissingEmployeeIdentifier_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Employee,
            TenantIdentifier = "acme-corp"
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Employee_WhitespaceEmployeeIdentifier_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Employee,
            TenantIdentifier = "acme-corp",
            EmployeeIdentifier = "   "
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    #endregion

    #region Workspace

    [Fact]
    public void Workspace_Default_IsEmptyAndValid()
    {
        var ctx = new IsolationContext { Level = IsolationLevel.MultiTenant };
        Assert.False(ctx.Workspace.IsConfigured);
        Assert.Equal(McpPermission.None, ctx.Workspace.Backend);
        ctx.Validate(); // must not throw
    }

    [Fact]
    public void Workspace_Invalid_Throws()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.MultiTenant,
            Workspace = new WorkspaceContext { Backend = McpPermission.Read }
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Workspace_Missing_Throws()
    {
        var ctx = new IsolationContext { Level = IsolationLevel.MultiTenant, Workspace = null };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    [Fact]
    public void Workspace_MultiTenant_AnyTenants_IsValid()
    {
        using var fixture = new WorkspaceFixture();
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.MultiTenant,
            Workspace = new WorkspaceContext
            {
                Roots = [fixture.Root],
                Backend = McpPermission.Write,
                Tenants = ["XX.Dev", "YY.Dev"]
            }
        };
        ctx.Validate(); // must not throw
    }

    [Fact]
    public void Workspace_TenantLevel_IsolatedTenant_IsValid()
    {
        using var fixture = new WorkspaceFixture();
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Tenant,
            TenantIdentifier = "XX.Dev",
            Workspace = new WorkspaceContext
            {
                Roots = [fixture.Root],
                Backend = McpPermission.Write,
                Tenants = ["xx.dev"]
            }
        };
        ctx.Validate(); // must not throw
    }

    [Fact]
    public void Workspace_TenantLevel_ForeignTenant_Throws()
    {
        using var fixture = new WorkspaceFixture();
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Tenant,
            TenantIdentifier = "XX.Dev",
            Workspace = new WorkspaceContext
            {
                Roots = [fixture.Root],
                Backend = McpPermission.Write,
                Tenants = ["XX.Dev", "YY.Dev"]
            }
        };
        Assert.Throws<InvalidOperationException>(ctx.Validate);
    }

    #endregion

    #region Regulator

    [Fact]
    public void Permissions_Default_RegulatorNoneOthersRead()
    {
        var permissions = new IsolationContext().Permissions;
        Assert.Equal(McpPermission.None, permissions.Regulator);
        Assert.Equal(McpPermission.Read, permissions.HR);
        Assert.Equal(McpPermission.Read, permissions.Payroll);
        Assert.Equal(McpPermission.Read, permissions.Report);
        Assert.Equal(McpPermission.Read, permissions.System);
    }

    [Fact]
    public void Regulator_Granted_MultiTenant_IsValid()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.MultiTenant,
            Permissions = new McpPermissions { Regulator = McpPermission.Read }
        };
        ctx.Validate(); // must not throw
        Assert.True(ctx.Permissions.IsGranted(McpRole.Regulator, McpPermission.Read));
    }

    [Fact]
    public void Regulator_Granted_Tenant_IsValid()
    {
        var ctx = new IsolationContext
        {
            Level = IsolationLevel.Tenant,
            TenantIdentifier = "XX.Dev",
            Permissions = new McpPermissions { Regulator = McpPermission.Read }
        };
        ctx.Validate(); // must not throw
        Assert.True(ctx.Permissions.IsGranted(McpRole.Regulator, McpPermission.Read));
    }

    #endregion
}