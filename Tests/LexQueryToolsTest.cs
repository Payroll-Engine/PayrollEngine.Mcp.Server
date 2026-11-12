using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Core.Isolation;
using PayrollEngine.Mcp.Tools.RegulatorTools;
using Xunit;

// the tools take the cancellation token as an optional argument; the tests never cancel through the runner
#pragma warning disable xUnit1051

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Tests of the Regulator read tools over invented satellites (test resources).
/// No backend, no network: URL checks and live verification are covered by the scope rules only.</summary>
public sealed class LexQueryToolsTest : IDisposable
{
    private const string Tax = "Data.Tax.2026";
    private const string Rates = "Data.Rates.2026";
    private const string Fees = "Data.Fees.2026";
    private const string Broken = "Data.Broken.2026";

    private readonly LexWorkspaceFixture workspace = new();

    public void Dispose() => workspace.Dispose();

    private LexQueryTools Tools(McpPermission regulator = McpPermission.Read) =>
        new(workspace.CreateIsolation(regulator));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement ForManifest(JsonElement array, string repository, string satellite)
    {
        var relative = LexWorkspaceFixture.Relative(repository, satellite);
        return array.EnumerateArray().Single(x => x.GetProperty("manifest").GetString() == relative);
    }

    private sealed class Recorder : IProgress<ProgressNotificationValue>
    {
        public List<ProgressNotificationValue> Values { get; } = [];
        public void Report(ProgressNotificationValue value) => Values.Add(value);
    }

    #region lex_list_manifests

    [Fact]
    public async Task List_ReturnsManifestsMatchingTheRepositoryMask()
    {
        var result = Parse(await Tools().ListManifestsAsync());

        var scope = result.GetProperty("scope");
        Assert.Equal(4, scope.GetProperty("matched").GetInt32());
        Assert.Equal(4, scope.GetProperty("processed").GetInt32());
        Assert.False(scope.GetProperty("truncated").GetBoolean());
        var manifests = result.GetProperty("manifests");
        Assert.Equal(4, manifests.GetArrayLength());
        Assert.DoesNotContain(manifests.EnumerateArray(), x => x.GetProperty("repository").GetString() == "Other");
    }

    [Fact]
    public async Task List_SummarizesManifestAndSnapshotState()
    {
        var manifests = Parse(await Tools().ListManifestsAsync()).GetProperty("manifests");

        var tax = ForManifest(manifests, "Repo.XX", Tax);
        Assert.Equal(Tax, tax.GetProperty("satellite").GetString());
        Assert.Equal("XX", tax.GetProperty("authority").GetString());
        Assert.Equal(2, tax.GetProperty("entries").GetInt32());
        Assert.Equal(1, tax.GetProperty("autoVerified").GetInt32());
        Assert.Equal(1, tax.GetProperty("withoutVerification").GetInt32());
        Assert.Equal("current", tax.GetProperty("snapshotState").GetString());
        Assert.StartsWith("sha256:", tax.GetProperty("contentHash").GetString());

        var rates = ForManifest(manifests, "Repo.XX", Rates);
        Assert.Equal("stale", rates.GetProperty("snapshotState").GetString());
        Assert.NotEmpty(rates.GetProperty("snapshotDifferences").EnumerateArray());

        Assert.Equal("missing", ForManifest(manifests, "Repo.YY", Fees).GetProperty("snapshotState").GetString());
        Assert.True(ForManifest(manifests, "Repo.YY", Broken).TryGetProperty("error", out _));
    }

    [Fact]
    public async Task List_NarrowsByRepositoryAndSatellite()
    {
        var byRepository = Parse(await Tools().ListManifestsAsync(repository: "Repo.YY"));
        Assert.Equal(2, byRepository.GetProperty("manifests").GetArrayLength());

        var bySatellite = Parse(await Tools().ListManifestsAsync(satellite: Tax.ToLowerInvariant()));
        var single = Assert.Single(bySatellite.GetProperty("manifests").EnumerateArray());
        Assert.Equal(LexWorkspaceFixture.Relative("Repo.XX", Tax), single.GetProperty("manifest").GetString());
    }

    [Fact]
    public async Task List_AcceptsTheConfiguredRoot()
    {
        var result = Parse(await Tools().ListManifestsAsync(root: workspace.Root));
        Assert.Equal(4, result.GetProperty("scope").GetProperty("matched").GetInt32());
    }

    [Fact]
    public async Task List_MaxManifests_TruncatesAndReportsTheScope()
    {
        var result = Parse(await Tools().ListManifestsAsync(maxManifests: 1));

        var scope = result.GetProperty("scope");
        Assert.Equal(4, scope.GetProperty("matched").GetInt32());
        Assert.Equal(1, scope.GetProperty("processed").GetInt32());
        Assert.Equal(1, scope.GetProperty("limit").GetInt32());
        Assert.True(scope.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, result.GetProperty("manifests").GetArrayLength());
    }

    [Fact]
    public async Task List_MaxManifests_IsCappedByTheHardLimit()
    {
        var result = Parse(await Tools().ListManifestsAsync(maxManifests: 100000));
        Assert.Equal(RegulatorLimits.ListManifests, result.GetProperty("scope").GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("Other")]
    [InlineData("..")]
    [InlineData("Repo.XX/2026")]
    public async Task List_RejectsRepositoryOutsideTheRules(string repository)
    {
        var result = Parse(await Tools().ListManifestsAsync(repository: repository));
        Assert.True(result.TryGetProperty("error", out _), repository);
    }

    [Fact]
    public async Task List_RejectsARootThatIsNotConfigured()
    {
        var result = Parse(await Tools().ListManifestsAsync(root: workspace.Outside));
        Assert.Contains("outside the workspace roots", result.GetProperty("error").GetString());

        var below = Parse(await Tools().ListManifestsAsync(root: Path.Combine(workspace.Root, "Repo.XX")));
        Assert.Contains("configured workspace root", below.GetProperty("error").GetString());
    }

    [Fact]
    public async Task List_SkipsManifestsReachedOverALinkOutsideTheWorkspace()
    {
        var foreign = Path.Combine(workspace.Outside, "Data.Foreign.2026", ".lex");
        Directory.CreateDirectory(foreign);
        await File.WriteAllTextAsync(Path.Combine(foreign, "lex.json"), "{}");
        WorkspaceFixture.CreateDirectoryLink(
            Path.Combine(workspace.Root, "Repo.XX", "2026", "Data.Linked.2026"),
            Path.Combine(workspace.Outside, "Data.Foreign.2026"));

        var result = Parse(await Tools().ListManifestsAsync());

        var scope = result.GetProperty("scope");
        Assert.Equal(4, scope.GetProperty("matched").GetInt32());
        Assert.Equal(1, scope.GetProperty("skippedOutsideWorkspace").GetInt32());
        Assert.DoesNotContain(result.GetProperty("manifests").EnumerateArray(),
            x => x.GetProperty("satellite").GetString() == "Data.Linked.2026");
    }

    [Fact]
    public async Task List_WithoutRoots_ReturnsAnError()
    {
        var tools = new LexQueryTools(new IsolationContext { Permissions = new McpPermissions { Regulator = McpPermission.Read } });
        var result = Parse(await tools.ListManifestsAsync());
        Assert.Contains("No workspace roots", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AllTools_RefuseWithoutRegulatorPermission()
    {
        var tools = Tools(McpPermission.None);
        var results = new[]
        {
            await tools.ListManifestsAsync(),
            await tools.GetEntryAsync(LexWorkspaceFixture.Relative("Repo.XX", Tax), "Rates.A"),
            await tools.CheckAsync(),
            await tools.VerifySnapshotAsync(),
            await tools.VerifySourceAsync(),
            await tools.ValidateAsync()
        };
        Assert.All(results, json => Assert.Equal(nameof(UnauthorizedAccessException), Parse(json).GetProperty("type").GetString()));
    }

    #endregion

    #region lex_get_entry

    [Fact]
    public async Task GetEntry_ReturnsEntryWithContext()
    {
        var result = Parse(await Tools().GetEntryAsync(LexWorkspaceFixture.Relative("Repo.XX", Tax), "Rates.A"));

        Assert.Equal("Rates.A", result.GetProperty("key").GetString());
        var entry = result.GetProperty("entry");
        Assert.Equal("12.75", entry.GetProperty("content").GetString());
        Assert.Equal("Example Act s. 1", entry.GetProperty("legalReference").GetString());
        Assert.Equal("auto", entry.GetProperty("verification").GetProperty("mode").GetString());
        Assert.Equal("https://example.org/tax/rates", Assert.Single(result.GetProperty("sources").EnumerateArray()).GetProperty("url").GetString());
        Assert.Equal("current", result.GetProperty("snapshotState").GetString());
        Assert.StartsWith("sha256:", result.GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task GetEntry_ChangedEntry_ReportsTheSnapshotState()
    {
        var result = Parse(await Tools().GetEntryAsync(LexWorkspaceFixture.Relative("Repo.XX", Rates), "Limits.A"));
        Assert.Equal("changed", result.GetProperty("snapshotState").GetString());
    }

    [Fact]
    public async Task GetEntry_AcceptsTheSatelliteFolder()
    {
        var result = Parse(await Tools().GetEntryAsync($"Repo.XX/2026/{Tax}", "Rates.B"));
        Assert.Equal("Rates.B", result.GetProperty("key").GetString());
        Assert.Equal(LexWorkspaceFixture.Relative("Repo.XX", Tax), result.GetProperty("manifest").GetString());
    }

    [Fact]
    public async Task GetEntry_UnknownKey_ReturnsAnError()
    {
        var result = Parse(await Tools().GetEntryAsync(LexWorkspaceFixture.Relative("Repo.XX", Tax), "Rates.Z"));
        Assert.Contains("not found", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetEntry_UnreadableManifest_ReturnsAnError()
    {
        var result = Parse(await Tools().GetEntryAsync(LexWorkspaceFixture.Relative("Repo.YY", Broken), "Rates.A"));
        Assert.Contains("cannot be read", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("../outside/Data.Foreign.2026/.lex/lex.json")]
    [InlineData("Other/2026/Data.Hidden.2026/.lex/lex.json")]
    [InlineData("Repo.XX/2026/Data.Tax.2026/Regulation/Data.Tax.json")]
    public async Task GetEntry_RejectsPathsOutsideTheRules(string manifest)
    {
        var result = Parse(await Tools().GetEntryAsync(manifest, "Rates.A"));
        Assert.True(result.TryGetProperty("error", out _), manifest);
    }

    #endregion

    #region lex_check

    [Fact]
    public async Task Check_Development_ReportsFindingsPerManifest()
    {
        var result = Parse(await Tools().CheckAsync());

        Assert.Equal("development", result.GetProperty("mode").GetString());
        var results = result.GetProperty("results");
        Assert.Equal(4, results.GetArrayLength());
        Assert.False(ForManifest(results, "Repo.XX", Tax).GetProperty("hasFindings").GetBoolean());
        Assert.True(ForManifest(results, "Repo.YY", Fees).GetProperty("hasFindings").GetBoolean());
        Assert.True(ForManifest(results, "Repo.YY", Broken).TryGetProperty("error", out _));

        var summary = result.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("withoutFindings").GetInt32());
        Assert.Equal(3, summary.GetProperty("withFindings").GetInt32());
    }

    [Fact]
    public async Task Check_ResultsCarryNoAbsolutePaths()
    {
        var json = await Tools().CheckAsync();
        Assert.DoesNotContain(workspace.BasePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.BasePath.Replace('\\', '/'), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_Release_VerifiesTheLocalEvidenceAndRequiresLocation()
    {
        var result = Parse(await Tools().CheckAsync(repository: "Repo.XX", mode: "RELEASE"));

        Assert.Equal("release", result.GetProperty("mode").GetString());
        var tax = ForManifest(result.GetProperty("results"), "Repo.XX", Tax);
        Assert.True(tax.GetProperty("release").GetBoolean());
        Assert.True(tax.GetProperty("sourcesVerified").GetBoolean());
        Assert.True(tax.GetProperty("requireVerification").GetBoolean());
        // Rates.B has no verification record
        Assert.True(tax.GetProperty("hasFindings").GetBoolean());
        Assert.Equal(1, tax.GetProperty("unverified").GetInt32());
    }

    [Fact]
    public async Task Check_DevelopmentToggles_MakeMissingVerificationAFinding()
    {
        var result = Parse(await Tools().CheckAsync(satellite: Tax, requireVerification: true));
        var tax = Assert.Single(result.GetProperty("results").EnumerateArray());
        Assert.True(tax.GetProperty("hasFindings").GetBoolean());
        Assert.True(tax.GetProperty("requireVerification").GetBoolean());
    }

    [Fact]
    public async Task Check_UnknownMode_ReturnsAnError()
    {
        var result = Parse(await Tools().CheckAsync(mode: "strict"));
        Assert.Contains("Unknown mode", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Check_ReportsProgressPerManifest()
    {
        var progress = new Recorder();

        await Tools().CheckAsync(repository: "Repo.XX", progress: progress);

        // one notification before each manifest and one at the end
        Assert.Equal(3, progress.Values.Count);
        Assert.Equal([0f, 1f, 2f], progress.Values.Select(x => x.Progress).ToArray());
        Assert.All(progress.Values, x => Assert.Equal(2f, x.Total));
        Assert.Contains("Data.Tax.2026", progress.Values[0].Message + progress.Values[1].Message);
    }

    [Fact]
    public async Task Check_UrlCheck_RequiresANarrowScope_BeforeAnyRequest()
    {
        var result = Parse(await Tools().CheckAsync(checkUrls: true));
        Assert.Contains("repository", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Check_Cancelled_ThrowsInsteadOfReturningAnError()
    {
        using var cancellation = new System.Threading.CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Tools().CheckAsync(cancellationToken: cancellation.Token));
    }

    #endregion

    #region lex_verify_snapshot

    [Fact]
    public async Task VerifySnapshot_ReportsCurrentStaleAndMissing()
    {
        var result = Parse(await Tools().VerifySnapshotAsync());

        var results = result.GetProperty("results");
        Assert.Equal("current", ForManifest(results, "Repo.XX", Tax).GetProperty("state").GetString());
        var rates = ForManifest(results, "Repo.XX", Rates);
        Assert.Equal("stale", rates.GetProperty("state").GetString());
        Assert.True(rates.GetProperty("differenceCount").GetInt32() > 0);
        Assert.NotEmpty(rates.GetProperty("differences").EnumerateArray());
        Assert.Equal("missing", ForManifest(results, "Repo.YY", Fees).GetProperty("state").GetString());
        Assert.Equal("missing", ForManifest(results, "Repo.YY", Broken).GetProperty("state").GetString());
    }

    #endregion

    #region lex_verify_source

    [Fact]
    public async Task VerifySource_Local_RatesEntriesWithoutWriting()
    {
        var lexPath = workspace.LexPath("Repo.XX", Tax);
        var before = ManifestHash.OfFile(lexPath);

        var result = Parse(await Tools().VerifySourceAsync(satellite: Tax));

        Assert.Equal("local", result.GetProperty("source").GetString());
        Assert.False(result.GetProperty("written").GetBoolean());
        var tax = Assert.Single(result.GetProperty("results").EnumerateArray());
        Assert.Equal(2, tax.GetProperty("entries").GetInt32());
        Assert.Equal(1, tax.GetProperty("counts").GetProperty("found").GetInt32());
        // rate B (10) is not in the evidence file (8)
        var listed = Assert.Single(tax.GetProperty("results").EnumerateArray());
        Assert.Equal("Rates.B", listed.GetProperty("key").GetString());
        Assert.NotEqual("found", listed.GetProperty("match").GetString());

        Assert.Equal(before, ManifestHash.OfFile(lexPath));
        Assert.False(File.Exists(Path.Combine(workspace.Satellite("Repo.XX", Tax), "Regulation", "lex.json")));
    }

    [Fact]
    public async Task VerifySource_OnlyProblemsOff_ListsAllEntries()
    {
        var result = Parse(await Tools().VerifySourceAsync(satellite: Tax, onlyProblems: false));
        var tax = Assert.Single(result.GetProperty("results").EnumerateArray());
        Assert.Equal(2, tax.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task VerifySource_ReportsProgressPerManifest()
    {
        var progress = new Recorder();
        await Tools().VerifySourceAsync(repository: "Repo.XX", progress: progress);
        Assert.Equal(3, progress.Values.Count);
        Assert.Equal(2f, progress.Values[^1].Total);
    }

    [Fact]
    public async Task VerifySource_Live_RequiresANarrowScope_BeforeAnyRequest()
    {
        var result = Parse(await Tools().VerifySourceAsync(source: "live"));
        Assert.Contains("repository", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task VerifySource_UnknownSource_ReturnsAnError()
    {
        var result = Parse(await Tools().VerifySourceAsync(source: "cache"));
        Assert.Contains("Unknown source", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task VerifySource_UnreadableManifest_ReportsTheErrorPerManifest()
    {
        var result = Parse(await Tools().VerifySourceAsync(satellite: Broken));
        var broken = Assert.Single(result.GetProperty("results").EnumerateArray());
        Assert.True(broken.TryGetProperty("error", out _));
    }

    #endregion

    #region lex_validate

    [Fact]
    public async Task Validate_ReportsValidAndInvalidManifests()
    {
        var result = Parse(await Tools().ValidateAsync());

        var results = result.GetProperty("results");
        Assert.True(ForManifest(results, "Repo.XX", Tax).GetProperty("valid").GetBoolean());
        Assert.Empty(ForManifest(results, "Repo.XX", Tax).GetProperty("errors").EnumerateArray());
        var broken = ForManifest(results, "Repo.YY", Broken);
        Assert.False(broken.GetProperty("valid").GetBoolean());
        Assert.Contains("could not be parsed", broken.GetProperty("errors")[0].GetString());
        Assert.Equal(3, result.GetProperty("valid").GetInt32());
        Assert.Equal(1, result.GetProperty("invalid").GetInt32());
    }

    #endregion

    #region Limits

    [Theory]
    [InlineData(null, 50, 50)]
    [InlineData(0, 50, 50)]
    [InlineData(-3, 50, 50)]
    [InlineData(7, 50, 7)]
    [InlineData(500, 50, 50)]
    public void Limits_Apply(int? requested, int limit, int expected) =>
        Assert.Equal(expected, RegulatorLimits.Apply(requested, limit));

    [Fact]
    public void Limits_ExpensiveOperationsAreLowerThanCheapOnes()
    {
        Assert.True(RegulatorLimits.CheckUrls < RegulatorLimits.CheckRelease);
        Assert.True(RegulatorLimits.CheckRelease < RegulatorLimits.Check);
        Assert.True(RegulatorLimits.VerifySourceLive < RegulatorLimits.VerifySourceLocal);
    }

    #endregion

    #region Tool registration

    private static readonly string[] ToolNames =
    [
        "lex_list_manifests", "lex_get_entry", "lex_check", "lex_verify_snapshot", "lex_verify_source", "lex_validate"
    ];

    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(LexQueryTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttribute<McpServerToolAttribute>() != null);

    [Fact]
    public void Tools_AreNamedAsDocumented_AndRoleIsRegulator()
    {
        var names = ToolMethods().Select(x => x.GetCustomAttribute<McpServerToolAttribute>()!.Name).Order().ToArray();
        Assert.Equal(ToolNames.Order().ToArray(), names);
        Assert.Equal(McpRole.Regulator, typeof(LexQueryTools).GetCustomAttribute<ToolRoleAttribute>()!.Role);
        Assert.Equal(McpPermission.Read, typeof(LexQueryTools).GetCustomAttribute<ToolRoleAttribute>()!.Required);
    }

    [Fact]
    public void Tools_AreReadOnly_AndHideProgressAndCancellationFromTheSchema()
    {
        var tools = new LexQueryTools(workspace.CreateIsolation());
        foreach (var method in ToolMethods())
        {
            var tool = McpServerTool.Create(method, tools);
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, tool.ProtocolTool.Name);
            var properties = tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name).ToList();
            Assert.DoesNotContain("progress", properties);
            Assert.DoesNotContain("cancellationToken", properties);
        }
    }

    #endregion
}
