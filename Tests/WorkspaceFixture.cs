using System;
using System.Diagnostics;
using System.IO;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Temporary workspace folder tree for workspace path tests.
/// Layout below <see cref="BasePath"/>:
/// <code>
/// root/Repo.XX/2026/Data.Tax/lex.json
/// root/Repo.YY/file.json
/// root/Other/secret.json
/// root/readme.md
/// root2/Repo.ZZ/file.json
/// rootX/Repo.XX/file.json   (not configured, prefix sibling of root)
/// outside/secret.json
/// </code></summary>
public sealed class WorkspaceFixture : IDisposable
{
    /// <summary>Repository mask of the fixture workspace</summary>
    public const string RepoMask = "Repo.*";

    /// <summary>Temporary base folder</summary>
    public string BasePath { get; }

    /// <summary>First configured root</summary>
    public string Root { get; }

    /// <summary>Second configured root</summary>
    public string Root2 { get; }

    /// <summary>Folder outside all roots</summary>
    public string Outside { get; }

    /// <summary>Folder next to the first root whose name starts with the root name</summary>
    public string PrefixSibling { get; }

    public WorkspaceFixture()
    {
        BasePath = Path.Combine(Path.GetTempPath(), "mcp-workspace-" + Guid.NewGuid().ToString("N"));
        Root = Path.Combine(BasePath, "root");
        Root2 = Path.Combine(BasePath, "root2");
        Outside = Path.Combine(BasePath, "outside");
        PrefixSibling = Path.Combine(BasePath, "rootX");

        CreateFile(Root, "Repo.XX", "2026", "Data.Tax", "lex.json");
        CreateFile(Root, "Repo.YY", "file.json");
        CreateFile(Root, "Other", "secret.json");
        CreateFile(Root, "readme.md");
        CreateFile(Root2, "Repo.ZZ", "file.json");
        CreateFile(PrefixSibling, "Repo.XX", "file.json");
        CreateFile(Outside, "secret.json");
    }

    /// <summary>Workspace over both roots with the repository mask</summary>
    public WorkspaceContext CreateWorkspace() => new()
    {
        Roots = [Root, Root2],
        RepoMask = RepoMask
    };

    /// <summary>Combines path segments below the base folder</summary>
    public string Combine(params string[] segments) =>
        Path.Combine([BasePath, .. segments]);

    /// <summary>Creates a directory link (symbolic link, on Windows with junction fallback).
    /// Skips the calling test when the environment does not permit links.</summary>
    /// <param name="link">Path of the link</param>
    /// <param name="target">Target folder (maybe missing)</param>
    public static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // symbolic links require a privilege on Windows
        }

        if (OperatingSystem.IsWindows() && CreateJunction(link, target))
        {
            return;
        }
        Xunit.Assert.Skip("Directory links cannot be created in this environment.");
    }

    /// <summary>Creates a file symbolic link.
    /// Skips the calling test when the environment does not permit links.</summary>
    /// <param name="link">Path of the link</param>
    /// <param name="target">Target file (maybe missing)</param>
    public static void CreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Xunit.Assert.Skip("File links cannot be created in this environment.");
        }
    }

    private static bool CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return false;
        }
        process.WaitForExit();
        return process.ExitCode == 0 && new DirectoryInfo(link).LinkTarget != null;
    }

    private static void CreateFile(string folder, params string[] segments)
    {
        var path = Path.Combine([folder, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }

    public void Dispose()
    {
        if (!Directory.Exists(BasePath))
        {
            return;
        }

        // remove links first: recursive delete fails on junctions
        RemoveLinks(BasePath);
        Directory.Delete(BasePath, recursive: true);
    }

    private static void RemoveLinks(string folder)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0 };
        foreach (var path in Directory.GetFileSystemEntries(folder, "*", options))
        {
            var info = new FileInfo(path);
            var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
            if (info.LinkTarget != null)
            {
                if (isDirectory)
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }
            }
            else if (isDirectory)
            {
                RemoveLinks(path);
            }
        }
    }
}
