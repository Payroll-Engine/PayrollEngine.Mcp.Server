using System;
using System.IO;
using System.Threading.Tasks;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Temporary workspace with invented data satellites, copied from the test resources.
/// Layout below <see cref="Root"/>:
/// <code>
/// Repo.XX/2026/Data.Tax.2026     manifest, evidence file, current snapshot
/// Repo.XX/2026/Data.Rates.2026   manifest changed after the snapshot (stale snapshot)
/// Repo.YY/2026/Data.Fees.2026    manifest without snapshot
/// Repo.YY/2026/Data.Broken.2026  manifest that is not valid JSON
/// Other/2026/Data.Hidden.2026    repository not matching the repository mask
/// </code></summary>
public sealed class LexWorkspaceFixture : IDisposable
{
    /// <summary>Repository mask of the fixture workspace</summary>
    public const string RepoMask = "Repo.*";

    /// <summary>Temporary base folder</summary>
    public string BasePath { get; } = Path.Combine(Path.GetTempPath(), "mcp-lex-" + Guid.NewGuid().ToString("N"));

    /// <summary>The configured workspace root</summary>
    public string Root => Path.Combine(BasePath, "root");

    /// <summary>A folder outside the workspace roots</summary>
    public string Outside => Path.Combine(BasePath, "outside");

    public LexWorkspaceFixture()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Resources", "LexWorkspace");
        Copy(source, Root);
        Directory.CreateDirectory(Outside);

        CreateSnapshot(Satellite("Repo.XX", "Data.Tax.2026"));
        CreateSnapshot(Satellite("Repo.XX", "Data.Rates.2026"));

        // change the manifest after the snapshot: the snapshot is stale
        var rates = LexPath("Repo.XX", "Data.Rates.2026");
        File.WriteAllText(rates, File.ReadAllText(rates).Replace("\"content\": \"11\"", "\"content\": \"12\""));
    }

    /// <summary>Workspace over the root with the repository mask</summary>
    public WorkspaceContext CreateWorkspace() => new()
    {
        Roots = [Root],
        RepoMask = RepoMask
    };

    /// <summary>Isolation context with the Regulator permission</summary>
    public IsolationContext CreateIsolation(McpPermission regulator = McpPermission.Read) => new()
    {
        Permissions = new McpPermissions { Regulator = regulator },
        Workspace = CreateWorkspace()
    };

    /// <summary>Folder of a satellite</summary>
    public string Satellite(string repository, string satellite) =>
        Path.Combine(Root, repository, "2026", satellite);

    /// <summary>Full path of the lex.json of a satellite</summary>
    public string LexPath(string repository, string satellite) =>
        Path.Combine(Satellite(repository, satellite), ".lex", "lex.json");

    /// <summary>Path of the lex.json of a satellite relative to the root, as returned by the tools</summary>
    public static string Relative(string repository, string satellite) =>
        $"{repository}/2026/{satellite}/.lex/lex.json";

    private static void CreateSnapshot(string satelliteFolder)
    {
        var result = Task.Run(() => SnapshotOperation.RunAsync(Path.Combine(satelliteFolder, ".lex", "lex.json")))
            .GetAwaiter().GetResult();
        if (result.Status != SnapshotStatus.Created)
        {
            throw new InvalidOperationException(
                $"Test snapshot of '{satelliteFolder}' failed: {result.Status} {string.Join("; ", result.ValidationErrors ?? [])} {result.ParseError}");
        }
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            Copy(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(BasePath))
        {
            return;
        }
        // links first: recursive delete fails on junctions
        foreach (var path in Directory.GetFileSystemEntries(BasePath, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget != null && info.Attributes.HasFlag(FileAttributes.Directory))
            {
                Directory.Delete(path);
            }
        }
        Directory.Delete(BasePath, recursive: true);
    }
}
