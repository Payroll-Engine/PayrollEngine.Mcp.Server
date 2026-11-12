using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayrollEngine.Client;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;
using PayrollEngine.Mcp.Tools;
using PayrollEngine.Serilog;

namespace PayrollEngine.Mcp.Server;

sealed class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Remove ALL default logging providers — stdout is reserved for the MCP protocol.
            // Any output to stdout (including .NET host startup messages) corrupts the JSON stream.
            // Serilog (file-only) is configured separately via SetupSerilog().
            builder.Logging.ClearProviders();

            // logger setup — file only, no console (stdio is reserved for MCP protocol)
            builder.Configuration.SetupSerilog();

            // isolation context — reads McpServer:IsolationLevel, TenantIdentifier, Permissions and the
            // workspace (McpServer:Regulator); validates the isolation level, the workspace and the Regulator roots
            var isolationContext = HostStartup.CreateIsolationContext(builder.Configuration);
            LogIsolationContext(isolationContext);

            // payroll http client for backend communication — optional:
            // required only by a backend role (HR, Payroll, Report, System) or McpServer:Regulator:Backend
            PayrollHttpClient payrollHttpClient = null;
            if (HostStartup.RequiresBackend(isolationContext.Permissions, isolationContext.Workspace))
            {
                payrollHttpClient = await BuildPayrollHttpClientAsync(builder,
                    HostStartup.DescribeBackendRequirement(isolationContext.Permissions, isolationContext.Workspace));
            }
            else
            {
                Log.Information("Backend: offline (no backend role and no Regulator backend access granted)");
            }

            // MCP server with stdio transport and permission-filtered tool registration
            builder.Services.AddSingleton(isolationContext);
            if (payrollHttpClient != null)
            {
                builder.Services.AddSingleton(payrollHttpClient);
            }
            var mcpBuilder = builder.Services
                .AddMcpServer()
                .WithStdioServerTransport();

            RegisterPermittedTools(mcpBuilder, isolationContext.Permissions, isolationContext.Level, payrollHttpClient != null);

            await builder.Build().RunAsync();
        }
        catch (Exception exception)
        {
            // write to stderr — stdout is reserved for MCP protocol, Serilog may not be initialized
            await Console.Error.WriteLineAsync($"[FATAL] {exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}");
            await Console.Error.WriteLineAsync(exception.ToString());
            try { Log.Critical(exception, exception.GetBaseException().Message); } catch { /* Serilog not available */ }
            // a failed start must be visible to the MCP client and to scripts
            Environment.ExitCode = 1;
        }
    }

    // ReSharper disable once GrammarMistakeInComment
    /// <summary>Registers only the tool classes permitted by the active McpPermissions.
    /// Uses the WithTools&lt;T&gt;() extension method from the MCP SDK via reflection
    /// to support dynamic type-based registration.</summary>
    private static void RegisterPermittedTools(IMcpServerBuilder mcpBuilder, McpPermissions permissions,
        IsolationLevel isolationLevel, bool backendConnected)
    {
        var toolsAssembly = typeof(ToolsMarker).Assembly;
        var permittedTypes = ToolRegistrar.GetPermittedTypes(toolsAssembly, permissions, isolationLevel).ToList();
        HostStartup.EnsureBackendIndependentTools(permittedTypes, backendConnected);

        // Locate WithTools<T>(IMcpServerBuilder) in the MCP SDK via reflection.
        // Searches both the main assembly and ModelContextProtocol.Core for the generic overload.
        var searchAssemblies = new[]
        {
            typeof(IMcpServerBuilder).Assembly,
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ModelContextProtocol.Core")
        }.Where(a => a != null).ToArray();

        var withToolsMethod = searchAssemblies
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m =>
                m.Name == "WithTools" &&
                m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 &&
                m.GetParameters().Length >= 1 &&
                m.GetParameters()[0].ParameterType == typeof(IMcpServerBuilder));

        if (withToolsMethod == null)
        {
            // Diagnostic: list all WithTools overloads found
            var all = searchAssemblies
                .SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.Name == "WithTools")
                .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) generic={m.IsGenericMethodDefinition} gargs={m.GetGenericArguments().Length}")
                .ToList();
            throw new InvalidOperationException(
                $"WithTools<T>(IMcpServerBuilder) not found in the MCP SDK. Found overloads: {string.Join(" | ", all)}");
        }

        // Build the parameter array — first param is IMcpServerBuilder, remaining are optional (Type.Missing)
        var paramCount = withToolsMethod.GetParameters().Length;
        foreach (var type in permittedTypes)
        {
            var invokeArgs = new object[paramCount];
            invokeArgs[0] = mcpBuilder;
            for (var i = 1; i < paramCount; i++)
            {
                invokeArgs[i] = Type.Missing;
            }
            withToolsMethod.MakeGenericMethod(type).Invoke(null, invokeArgs);
        }

        Log.Information($"Registered {permittedTypes.Count} tool class(es) based on active permissions.");
    }

    private static async Task<PayrollHttpClient> BuildPayrollHttpClientAsync(
        HostApplicationBuilder builder, string requirement)
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        // insecure ssl: skip certificate validation (dev only)
        var allowInsecureSsl = builder.Configuration["AllowInsecureSsl"];
        if (bool.TryParse(allowInsecureSsl, out var skipSsl) && skipSsl)
        {
            Log.Warning("SSL certificate validation is disabled (AllowInsecureSsl=true)");
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        // http configuration from appsettings.json / apisettings.json / environment variables
        var config = await builder.Configuration.GetHttpConfigurationAsync();
        if (config == null || !config.Valid())
        {
            throw new InvalidOperationException(
                $"Missing or invalid Payroll HTTP client configuration; the backend connection is {requirement}. " +
                "Set ApiSettings in appsettings.json or the environment variable " +
                $"'{SystemSpecification.PayrollApiConnection}', or run offline by granting only the Regulator role " +
                "(McpServer:Permissions: HR, Payroll, Report and System set to None; McpServer:Regulator:Backend None).");
        }

        var client = new PayrollHttpClient(handler, config);

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            client.SetApiKey(config.ApiKey);
        }

        return client;
    }

    private static void LogIsolationContext(IsolationContext isolation)
    {
        var level = isolation.Level.ToString();
        var detail = isolation.Level switch
        {
            IsolationLevel.MultiTenant => "full access across all tenants",
            IsolationLevel.Tenant      => $"tenant: {isolation.TenantIdentifier}",
            IsolationLevel.Division    => $"tenant: {isolation.TenantIdentifier}, division: {isolation.DivisionName}",
            IsolationLevel.Employee    => $"tenant: {isolation.TenantIdentifier}, employee: {isolation.EmployeeIdentifier}",
            _                          => "unknown"
        };
        Log.Information($"Isolation: {level} ({detail})");
        if (isolation.Permissions.Regulator != McpPermission.None)
        {
            Log.Information($"Regulator: {isolation.Permissions.Regulator} " +
                            $"(roots: {isolation.Workspace.Roots.Count}, backend: {isolation.Workspace.Backend})");
        }
    }
}
