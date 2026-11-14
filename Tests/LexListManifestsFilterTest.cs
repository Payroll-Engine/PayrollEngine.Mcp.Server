using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Core.Isolation;
using PayrollEngine.Mcp.Tools.RegulatorTools;
using Xunit;

// the tools take the cancellation token as an optional argument; the tests never cancel through the runner
#pragma warning disable xUnit1051

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Tests of the filters and the summary of lex_list_manifests over invented satellites</summary>
public sealed class LexListManifestsFilterTest : IDisposable
{
    private const string Tax = "Data.Tax.2026";
    private const string Rates = "Data.Rates.2026";
    private const string Fees = "Data.Fees.2026";
    private const string Broken = "Data.Broken.2026";

    private readonly LexWorkspaceFixture workspace = new();

    public void Dispose() => workspace.Dispose();

    // ReSharper disable once RedundantArgumentDefaultValue
    private LexQueryTools Tools() => new(workspace.CreateIsolation(McpPermission.Read));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task List_Default_CarriesTheSummaryOverAllManifests()
    {
        var result = Parse(await Tools().ListManifestsAsync());

        var summary = result.GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("manifests").GetInt32());
        Assert.Equal(1, summary.GetProperty("withError").GetInt32());
        var states = summary.GetProperty("snapshotStates");
        Assert.Equal(1, states.GetProperty("current").GetInt32());
        Assert.Equal(1, states.GetProperty("stale").GetInt32());
        // a manifest that cannot be read has no snapshot state of its own: it counts as missing
        Assert.Equal(2, states.GetProperty("missing").GetInt32());
        Assert.Equal(0, states.GetProperty("unreadable").GetInt32());
        Assert.Equal(4, result.GetProperty("listed").GetInt32());
        Assert.Equal(4, result.GetProperty("manifests").GetArrayLength());
        Assert.False(result.TryGetProperty("sizeWarning", out _));
    }

    [Fact]
    public async Task List_Default_RowsKeepTheFullShape()
    {
        var row = Parse(await Tools().ListManifestsAsync()).GetProperty("manifests").EnumerateArray()
            .Single(x => x.GetProperty("manifest").GetString() == LexWorkspaceFixture.Relative("Repo.XX", Tax));

        foreach (var name in new[]
                 {
                     "repository", "satellite", "authority", "regulation", "validFrom", "provider", "languages", "entries",
                     "openEntries", "autoVerified", "manualVerified", "withoutVerification", "sources", "files", "exclusions",
                     "snapshotState", "snapshotDifferences", "contentHash"
                 })
        {
            Assert.True(row.TryGetProperty(name, out _), name);
        }
    }

    [Fact]
    public async Task List_OnlyProblems_ListsTheManifestsWithAProblem_SummaryCoversAll()
    {
        var result = Parse(await Tools().ListManifestsAsync(onlyProblems: true));

        var listed = result.GetProperty("manifests").EnumerateArray()
            .Select(x => x.GetProperty("satellite").GetString()).ToList();
        // Tax: current snapshot, but one entry without verification; Rates: stale; Fees: missing snapshot; Broken: error
        Assert.Contains(Tax, listed);
        Assert.Contains(Rates, listed);
        Assert.Contains(Fees, listed);
        Assert.Contains(Broken, listed);
        Assert.Equal(listed.Count, result.GetProperty("listed").GetInt32());
        Assert.Equal(4, result.GetProperty("summary").GetProperty("manifests").GetInt32());
    }

    [Fact]
    public async Task List_OnlyProblems_OmitsAManifestWithoutProblem()
    {
        // make Tax clean: verify the second entry and renew the snapshot
        var lexPath = workspace.LexPath("Repo.XX", Tax);
        var text = await File.ReadAllTextAsync(lexPath);
        const string marker = "\"legalReference\": \"Example Act s. 2\"";
        Assert.Contains(marker, text);
        await File.WriteAllTextAsync(lexPath, text.Replace(marker, marker +
            ", \"verification\": { \"mode\": \"auto\", \"verifiedOn\": \"2026-01-10\", \"evidence\": \"rates.html\", \"method\": \"Example verifier 1.0\" }"));
        var snapshot = await SnapshotOperation.RunAsync(lexPath, null, false, true);
        Assert.Equal(SnapshotStatus.Created, snapshot.Status);

        var result = Parse(await Tools().ListManifestsAsync(onlyProblems: true));

        var listed = result.GetProperty("manifests").EnumerateArray()
            .Select(x => x.GetProperty("satellite").GetString()).ToList();
        Assert.DoesNotContain(Tax, listed);
        Assert.Equal(3, listed.Count);
        // the summary still covers the clean manifest
        Assert.Equal(4, result.GetProperty("summary").GetProperty("manifests").GetInt32());
        Assert.Equal(3, result.GetProperty("summary").GetProperty("withProblems").GetInt32());
    }

    [Fact]
    public async Task List_SummaryOnly_ListsCompactRows()
    {
        var result = Parse(await Tools().ListManifestsAsync(summaryOnly: true));

        var rows = result.GetProperty("manifests").EnumerateArray().ToList();
        Assert.Equal(4, rows.Count);
        var tax = rows.Single(x => x.GetProperty("satellite").GetString() == Tax);
        Assert.Equal(2, tax.GetProperty("entries").GetInt32());
        Assert.Equal(1, tax.GetProperty("withoutVerification").GetInt32());
        Assert.Equal("current", tax.GetProperty("snapshotState").GetString());
        Assert.StartsWith("sha256:", tax.GetProperty("contentHash").GetString());
        // the compact row carries no descriptive fields
        Assert.False(tax.TryGetProperty("sources", out _));
        Assert.False(tax.TryGetProperty("snapshotDifferences", out _));
        Assert.False(tax.TryGetProperty("languages", out _));
    }

    [Fact]
    public async Task List_CountsOnly_ListsNoRows()
    {
        var result = Parse(await Tools().ListManifestsAsync(countsOnly: true));

        Assert.False(result.TryGetProperty("manifests", out _));
        Assert.Equal(0, result.GetProperty("listed").GetInt32());
        Assert.Equal(4, result.GetProperty("summary").GetProperty("manifests").GetInt32());
        Assert.Equal(4, result.GetProperty("scope").GetProperty("processed").GetInt32());
    }

    [Fact]
    public async Task List_LargeScope_AddsASizeWarningThatNamesTheFilters()
    {
        // copies of one satellite below a new repository: the full rows exceed the size hint
        var source = workspace.Satellite("Repo.YY", Fees);
        for (var i = 0; i < 150; i++)
        {
            CopyDirectory(source, Path.Combine(workspace.Root, "Repo.ZZ", "2026", $"Data.Copy{i:000}.2026"));
        }

        var json = await Tools().ListManifestsAsync(repository: "Repo.ZZ");
        var result = Parse(json);

        Assert.True(json.Length > RegulatorLimits.ResponseSizeHint);
        var warning = result.GetProperty("sizeWarning").GetString();
        Assert.Contains("onlyProblems", warning);
        Assert.Contains("summaryOnly", warning);
        Assert.Contains("countsOnly", warning);

        // the compact rows and the counts stay below the hint: no warning
        var compact = await Tools().ListManifestsAsync(repository: "Repo.ZZ", countsOnly: true);
        Assert.True(compact.Length < RegulatorLimits.ResponseSizeHint);
        Assert.False(Parse(compact).TryGetProperty("sizeWarning", out _));
        Assert.Equal(150, Parse(compact).GetProperty("summary").GetProperty("manifests").GetInt32());
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
