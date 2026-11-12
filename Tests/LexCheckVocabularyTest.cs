using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Tools.RegulatorTools;
using Xunit;
// ReSharper disable StringLiteralTypo

// the tools take the cancellation token as an optional argument; the tests never cancel through the runner
#pragma warning disable xUnit1051

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>lex_check in release mode reads the additional language folder of the host variable
/// (<see cref="VerifySourceOperation.VocabularyVariable"/>), as the release gate does. The variable is process wide:
/// the tests of this class run one after the other.</summary>
[Collection(Name)]
public sealed class LexCheckVocabularyTest : IDisposable
{
    internal const string Name = "LexVocabularyVariable";

    private const string Repository = "Repo.ZZ";
    private const string Satellite = "Data.Danish.2026";

    private const string Danish = """
        {
          "language": "da",
          "name": "Danish",
          "percentWords": ["procent"],
          "enumerationWords": ["og", "eller"],
          "rangeWords": ["til"],
          "validityBefore": ["med virkning fra"],
          "months": [["januar"], ["februar"], ["marts"], ["april"], ["maj"], ["juni"], ["juli"], ["august"],
            ["september"], ["oktober"], ["november"], ["december"]]
        }
        """;

    private readonly LexWorkspaceFixture workspace = new();
    private readonly string vocabulary;
    private readonly string previous = Environment.GetEnvironmentVariable(VerifySourceOperation.VocabularyVariable);

    public LexCheckVocabularyTest()
    {
        vocabulary = Path.Combine(workspace.BasePath, "vocabulary");
        Directory.CreateDirectory(vocabulary);
        File.WriteAllText(Path.Combine(vocabulary, "da.json"), Danish);
        WriteSatellite();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(VerifySourceOperation.VocabularyVariable, previous);
        workspace.Dispose();
    }

    private void WriteSatellite()
    {
        var folder = workspace.Satellite(Repository, Satellite);
        Directory.CreateDirectory(Path.Combine(folder, ".lex"));
        Directory.CreateDirectory(Path.Combine(folder, "Regulation"));
        File.WriteAllText(Path.Combine(folder, "Regulation", "Data.Danish.json"),
            """
            { "tenants": [ { "regulations": [ { "name": "XX.Data.Danish", "validFrom": "2026-01-01T00:00:00Z",
              "lookups": [ { "name": "Rates", "values": [ { "key": "A", "value": "12.75" } ] } ] } ] } ] }
            """);
        File.WriteAllText(Path.Combine(folder, ".lex", "rates.html"),
            "<html><body><h2>Satser</h2><p>Sats A er 12,75 procent.</p><h2>Noter</h2><p>" +
            string.Concat(Enumerable.Repeat("fyldtekst ", 60)) + "</p></body></html>");
        File.WriteAllText(workspace.LexPath(Repository, Satellite),
            """
            {
              "authority": "XX", "regulation": "XX.Data.Danish", "validFrom": "2026-01-01", "provider": "Example Authority",
              "languages": [ "da" ],
              "sources": [ { "url": "https://example.org/da/rates", "description": "Example rate table" } ],
              "values": [ {
                "key": "Rates.A", "content": "12.75", "sourceUrls": [ "https://example.org/da/rates" ],
                "documentRef": "rates.html", "sourceLocation": "rates.html: Section \"Satser\"", "legalReference": "Example Act s. 1",
                "verification": { "mode": "auto", "verifiedOn": "2026-01-10", "evidence": "rates.html", "method": "Example verifier 1.0" }
              } ],
              "files": [ { "fileName": "rates.html", "description": "Example rate table" } ]
            }
            """);
    }

    private Task<string> Check() =>
        new LexQueryTools(workspace.CreateIsolation()).CheckAsync(repository: Repository, mode: "release");

    [Fact]
    public async Task Release_LanguageWithoutFile_IsAnError()
    {
        Environment.SetEnvironmentVariable(VerifySourceOperation.VocabularyVariable, null);

        var result = JsonDocument.Parse(await Check()).RootElement;

        // the same refusal as the gate: the language of the manifest has no language file
        Assert.True(result.TryGetProperty("error", out var error), result.ToString());
        Assert.Contains("da", error.GetString());
    }

    [Fact]
    public async Task Release_VocabularyFromTheVariable_VerifiesTheAdditionalLanguage()
    {
        Environment.SetEnvironmentVariable(VerifySourceOperation.VocabularyVariable, vocabulary);

        var result = JsonDocument.Parse(await Check()).RootElement;

        Assert.False(result.TryGetProperty("error", out _), result.ToString());
        var satellite = Assert.Single(result.GetProperty("results").EnumerateArray());
        Assert.True(satellite.GetProperty("sourcesVerified").GetBoolean());
        Assert.Equal(0, satellite.GetProperty("ambiguous").GetInt32());
        Assert.Equal(0, satellite.GetProperty("notVerifiable").GetInt32());
    }
}
