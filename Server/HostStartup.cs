using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Server;

/// <summary>Startup rules of the host: isolation context, workspace and optional backend connection.
/// A deployment that grants no backend role and no backend access of the Regulator role
/// runs offline and needs no backend connection.</summary>
internal static class HostStartup
{
    /// <summary>Builds the isolation context from configuration (including the workspace)
    /// and validates it together with the Regulator rules.</summary>
    /// <param name="configuration">The application configuration</param>
    internal static IsolationContext CreateIsolationContext(IConfiguration configuration)
    {
        var level = ParseIsolationLevel(configuration["McpServer:IsolationLevel"]);

        var isolationContext = new IsolationContext
        {
            Level = level,
            TenantIdentifier = configuration["McpServer:TenantIdentifier"],
            DivisionName = configuration["McpServer:DivisionName"],
            EmployeeIdentifier = configuration["McpServer:EmployeeIdentifier"],
            PreviewUserIdentifier = configuration["McpServer:PreviewUserIdentifier"],
            IntegrityKey = configuration["McpServer:IntegrityKey"]
                           ?? Environment.GetEnvironmentVariable("PayrollIntegrityKey"),
            Permissions = McpPermissions.FromConfiguration(configuration),
            Workspace = WorkspaceContext.FromConfiguration(configuration)
        };

        isolationContext.Validate();
        ValidateRegulator(isolationContext.Permissions, isolationContext.Workspace);
        return isolationContext;
    }

    /// <summary>Reads the isolation level. A missing or empty value is MultiTenant; a value that is
    /// not an isolation level name stops the startup instead of opening the widest level.</summary>
    /// <param name="value">The configured value of McpServer:IsolationLevel</param>
    /// <exception cref="InvalidOperationException">The value is not an isolation level name</exception>
    internal static IsolationLevel ParseIsolationLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return IsolationLevel.MultiTenant;
        }

        // compare against the names: Enum.TryParse also accepts numbers and comma lists
        foreach (var name in Enum.GetNames<IsolationLevel>())
        {
            if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<IsolationLevel>(name);
            }
        }

        throw new InvalidOperationException(
            $"McpServer:IsolationLevel has the invalid value '{value}'. " +
            $"Allowed values: {string.Join(", ", Enum.GetNames<IsolationLevel>())}.");
    }

    /// <summary>True when a backend connection is required: a backend role (HR, Payroll, Report, System)
    /// is granted or the Regulator role has backend access (Regulator:Backend is not None).</summary>
    /// <param name="permissions">The role permissions</param>
    /// <param name="workspace">The workspace context</param>
    internal static bool RequiresBackend(McpPermissions permissions, WorkspaceContext workspace) =>
        permissions.HR != McpPermission.None ||
        permissions.Payroll != McpPermission.None ||
        permissions.Report != McpPermission.None ||
        permissions.System != McpPermission.None ||
        workspace.Backend != McpPermission.None;

    /// <summary>Describes why the backend connection is required, for the log and error messages</summary>
    /// <param name="permissions">The role permissions</param>
    /// <param name="workspace">The workspace context</param>
    internal static string DescribeBackendRequirement(McpPermissions permissions, WorkspaceContext workspace)
    {
        var reasons = new List<string>();
        if (permissions.HR != McpPermission.None) reasons.Add("Permissions:HR");
        if (permissions.Payroll != McpPermission.None) reasons.Add("Permissions:Payroll");
        if (permissions.Report != McpPermission.None) reasons.Add("Permissions:Report");
        if (permissions.System != McpPermission.None) reasons.Add("Permissions:System");
        if (workspace.Backend != McpPermission.None) reasons.Add($"{WorkspaceContext.SectionName}:Backend");
        return reasons.Count == 0
            ? "no backend role or backend access is granted"
            : $"required by {string.Join(", ", reasons)}";
    }

    /// <summary>Validates the Regulator role: it needs at least one workspace root.</summary>
    /// <param name="permissions">The role permissions</param>
    /// <param name="workspace">The workspace context</param>
    internal static void ValidateRegulator(McpPermissions permissions, WorkspaceContext workspace)
    {
        if (permissions.Regulator != McpPermission.None && !workspace.IsConfigured)
        {
            throw new InvalidOperationException(
                $"McpServer:Permissions:Regulator is '{permissions.Regulator}' but {WorkspaceContext.SectionName}:Roots " +
                "has no entry. Configure at least one workspace root folder, or set McpServer:Permissions:Regulator to None.");
        }
    }

    /// <summary>Fails when a tool class that needs the backend client is registered without a backend connection.</summary>
    /// <param name="toolTypes">The permitted tool classes</param>
    /// <param name="backendConnected">True when the backend connection is available</param>
    internal static void EnsureBackendIndependentTools(IEnumerable<Type> toolTypes, bool backendConnected)
    {
        if (backendConnected)
        {
            return;
        }

        var backendTools = toolTypes.Where(type => type.IsSubclassOf(typeof(ToolBase))).Select(type => type.Name).ToList();
        if (backendTools.Count > 0)
        {
            throw new InvalidOperationException(
                "The tool classes " + string.Join(", ", backendTools) +
                " need the backend, but no backend connection is configured.");
        }
    }
}
