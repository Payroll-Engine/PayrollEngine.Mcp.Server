using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Starts the real server process over stdio with a minimal PATH and runs the six Regulator read tools.
/// The server needs no console executable and no other tool: without git the check of the evidence versioning is
/// skipped and reported as a warning. No backend and no database required.</summary>
// the server process inherits the vocabulary variable that LexCheckVocabularyTest sets
[Collection(LexCheckVocabularyTest.Name)]
public class ServerProcessMinimalEnvironmentTest(LexWorkspaceFixture fixture) : IClassFixture<LexWorkspaceFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private const string Repository = "Repo.XX";
    private const string Satellite = "Data.Tax.2026";
    private const string GitWarning = "git not available: evidence versioning not checked";

    [Fact]
    public async Task ReadTools_PathWithoutConsoleExecutable_Succeed()
    {
        var path = PathWithout(["pecmd", "PayrollEngine.PayrollConsole"]);
        Assert.False(Resolves(path, "pecmd"), "The premise failed: pecmd is still on the PATH.");

        var checkResult = await RunReadToolsAsync(path);

        // with or without git on the machine: never a failure, git problems are warnings only
        Assert.DoesNotContain(checkResult.Warnings, x => x.Contains("did not answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadTools_PathWithoutGit_Succeed_AndWarnAboutTheMissingGit()
    {
        var path = PathWithout(["pecmd", "PayrollEngine.PayrollConsole", "git"]);
        Assert.False(Resolves(path, "git"), "The premise failed: git is still on the PATH.");

        var checkResult = await RunReadToolsAsync(path);

        // a clear warning instead of a silent pass, and no finding for it
        Assert.Contains(GitWarning, checkResult.Warnings);
        Assert.False(checkResult.HasEvidenceFinding);
    }

    #region Tool run

    private sealed record CheckOutcome(List<string> Warnings, bool HasEvidenceFinding);

    /// <summary>Starts the server with the PATH and calls the six read tools</summary>
    private async Task<CheckOutcome> RunReadToolsAsync(string path)
    {
        var settings = RegulatorOnlySettings();
        settings["PATH"] = path;
        using var process = StartServer(settings);
        try
        {
            await SendAsync(process, new
            {
                jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "minimal-environment-test", version = "1.0" }
                }
            });
            (await ReadResponseAsync(process, 1)).Dispose();
            await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" });

            var scope = new { repository = Repository, satellite = Satellite };
            var id = 2;

            var list = await CallAsync(process, id++, "lex_list_manifests", new { repository = Repository });
            Assert.Contains($"{Repository}/2026/{Satellite}/.lex/lex.json", list.ToString());

            var manifest = LexWorkspaceFixture.Relative(Repository, Satellite);
            var entry = await CallAsync(process, id++, "lex_get_entry", new { manifest, key = "Rates.A" });
            Assert.Contains("Rates.A", entry.ToString());

            var check = await CallAsync(process, id++, "lex_check", scope);
            var result = check.GetProperty("results")[0];
            var warnings = result.TryGetProperty("warnings", out var items)
                ? items.EnumerateArray().Select(x => x.GetString()).ToList()
                : [];
            var evidenceFinding = result.TryGetProperty("fileProblems", out var problems) &&
                                  problems.EnumerateArray().Any(x => x.GetString()!.Contains("git", StringComparison.OrdinalIgnoreCase));

            var snapshot = await CallAsync(process, id++, "lex_verify_snapshot", scope);
            Assert.Contains(Satellite, snapshot.ToString());

            var source = await CallAsync(process, id++, "lex_verify_source",
                new { repository = Repository, satellite = Satellite, source = "local" });
            Assert.Contains(Satellite, source.ToString());

            var validate = await CallAsync(process, id, "lex_validate", scope);
            Assert.Contains(Satellite, validate.ToString());

            return new CheckOutcome(warnings, evidenceFinding);
        }
        finally
        {
            StopServer(process);
        }
    }

    /// <summary>Calls a tool and returns the structured answer; a tool error fails the test</summary>
    private static async Task<JsonElement> CallAsync(Process process, int id, string tool, object arguments)
    {
        await SendAsync(process, new
        {
            jsonrpc = "2.0", id, method = "tools/call",
            @params = new { name = tool, arguments }
        });
        using var response = await ReadResponseAsync(process, id);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), $"{tool}: {result}");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        return JsonDocument.Parse(text!).RootElement.Clone();
    }

    #endregion

    #region PATH

    /// <summary>The PATH of the test host without the folders that hold one of the executables</summary>
    private static string PathWithout(string[] executables)
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(folder => !executables.Any(name => Holds(folder, name)));
        return string.Join(Path.PathSeparator, folders);
    }

    private static bool Resolves(string path, string executable) =>
        path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(folder => Holds(folder, executable));

    private static bool Holds(string folder, string name)
    {
        try
        {
            return new[] { string.Empty, ".exe", ".cmd", ".bat", ".dll" }
                .Any(extension => File.Exists(Path.Combine(folder.Trim('"'), name + extension)));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    #endregion

    #region Helpers

    /// <summary>Settings of a Regulator-only deployment as environment variables</summary>
    private Dictionary<string, string> RegulatorOnlySettings() => new()
    {
        ["McpServer__Permissions__HR"] = "None",
        ["McpServer__Permissions__Payroll"] = "None",
        ["McpServer__Permissions__Report"] = "None",
        ["McpServer__Permissions__System"] = "None",
        ["McpServer__Permissions__Regulator"] = "Read",
        ["McpServer__Regulator__Roots"] = fixture.Root,
        ["McpServer__Regulator__RepoMask"] = LexWorkspaceFixture.RepoMask,
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
        // the host stays reachable (it is started before the PATH of the server is reduced)
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
