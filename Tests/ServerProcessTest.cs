using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Starts the real server process over stdio.
/// A Regulator-only deployment runs offline and answers get_server_info;
/// an invalid Regulator configuration fails the start with a clear message and a non-zero exit code.
/// No backend and no database required.</summary>
// the server process inherits the vocabulary variable that LexCheckVocabularyTest sets
[Collection(LexCheckVocabularyTest.Name)]
public class ServerProcessTest(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task RegulatorOnly_StartsOffline_AndAnswersServerInfo()
    {
        using var process = StartServer(RegulatorOnlySettings(fixture.Root));
        try
        {
            await SendAsync(process, new
            {
                jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "startup-test", version = "1.0" }
                }
            });
            using var initialize = await ReadResponseAsync(process, 1);
            Assert.True(initialize.RootElement.TryGetProperty("result", out _), initialize.RootElement.ToString());

            await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" });
            await SendAsync(process, new
            {
                jsonrpc = "2.0", id = 2, method = "tools/call",
                @params = new { name = "get_server_info", arguments = new { } }
            });
            using var call = await ReadResponseAsync(process, 2);

            var text = call.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
            using var info = JsonDocument.Parse(text!);
            Assert.Equal("offline", info.RootElement.GetProperty("backend").GetString());
            Assert.Equal("Read", info.RootElement.GetProperty("regulator").GetProperty("mode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(info.RootElement.GetProperty("lexVersion").GetString()));

            await SendAsync(process, new { jsonrpc = "2.0", id = 3, method = "tools/list" });
            using var list = await ReadResponseAsync(process, 3);
            var names = new List<string>();
            foreach (var tool in list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
            {
                names.Add(tool.GetProperty("name").GetString());
            }
            Assert.Contains("get_server_info", names);
            Assert.Contains("lex_list_manifests", names);
            Assert.DoesNotContain("get_employee", names);
        }
        finally
        {
            StopServer(process);
        }
    }

    [Fact]
    public async Task RegulatorWithoutRoots_FailsTheStart_WithClearMessage()
    {
        var settings = RegulatorOnlySettings(fixture.Root);
        settings.Remove("McpServer__Regulator__Roots");

        using var process = StartServer(settings);
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.True(process.WaitForExit(Timeout), "The server did not stop.");

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Regulator:Roots", error);
    }

    #region Helpers

    /// <summary>Settings of a Regulator-only deployment as environment variables</summary>
    private Dictionary<string, string> RegulatorOnlySettings(string root) => new()
    {
        ["McpServer__Permissions__HR"] = "None",
        ["McpServer__Permissions__Payroll"] = "None",
        ["McpServer__Permissions__Report"] = "None",
        ["McpServer__Permissions__System"] = "None",
        ["McpServer__Permissions__Regulator"] = "Read",
        ["McpServer__Regulator__Roots"] = root,
        ["McpServer__Regulator__RepoMask"] = WorkspaceFixture.RepoMask,
        // keep the log of the test out of the program data folder
        ["Serilog__WriteTo__0__Args__configure__0__Args__path"] = Path.Combine(fixture.BasePath, "logs", "log-.log")
    };

    private static Process StartServer(Dictionary<string, string> environment)
    {
        var serverDll = Path.Combine(AppContext.BaseDirectory, "PayrollEngine.Mcp.Server.dll");
        Assert.True(File.Exists(serverDll), $"Server not found: {serverDll}");

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(serverDll);
        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }
        return Process.Start(info)!;
    }

    private static async Task SendAsync(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }

    /// <summary>Reads stdout lines until the response with the id arrives</summary>
    private static async Task<JsonDocument> ReadResponseAsync(Process process, int id)
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellation.Token);
            Assert.NotNull(line);
            // ReSharper disable once PossibleNullReferenceException
            if (!line.StartsWith('{'))
            {
                continue;
            }
            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var value) &&
                value.ValueKind == JsonValueKind.Number && value.GetInt32() == id)
            {
                return document;
            }
            document.Dispose();
        }
    }

    private static void StopServer(Process process)
    {
        try
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already exited
        }
    }

    #endregion
}
