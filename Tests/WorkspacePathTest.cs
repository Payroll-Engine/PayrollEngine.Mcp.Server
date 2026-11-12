using System;
using System.IO;
using PayrollEngine.Mcp.Core.Isolation;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Unit tests for WorkspaceContext.ResolvePath().
/// Verifies that path arguments resolve below the workspace roots only:
/// separators, parent segments, absolute paths, Windows and Linux specific forms, and links.</summary>
public sealed class WorkspacePathTest : IDisposable
{
    private readonly WorkspaceFixture fixture = new();
    private readonly WorkspaceContext workspace;

    public WorkspacePathTest()
    {
        workspace = fixture.CreateWorkspace();
        workspace.Validate();
    }

    public void Dispose() => fixture.Dispose();

    private static char Sep => Path.DirectorySeparatorChar;

    private void AssertDenied(string path) =>
        Assert.Throws<UnauthorizedAccessException>(() => workspace.ResolvePath(path));

    #region Relative paths

    [Theory]
    [InlineData("Repo.XX/2026/Data.Tax/lex.json")]
    [InlineData(@"Repo.XX\2026\Data.Tax\lex.json")]
    [InlineData(@"Repo.XX/2026\Data.Tax/lex.json")]
    [InlineData("./Repo.XX/2026/Data.Tax/lex.json")]
    [InlineData(@".\Repo.XX\.\2026\Data.Tax\lex.json")]
    [InlineData("Repo.XX//2026///Data.Tax/lex.json")]
    [InlineData("  Repo.XX/2026/Data.Tax/lex.json  ")]
    public void Relative_SeparatorVariants_ResolveToSameFile(string path)
    {
        var resolved = workspace.ResolvePath(path);

        var expected = Path.Combine(fixture.Root, "Repo.XX", "2026", "Data.Tax", "lex.json");
        Assert.Equal(expected, resolved.FullPath);
        Assert.Equal(expected, resolved.RealPath);
        Assert.Equal(fixture.Root, resolved.Root);
        Assert.Equal("Repo.XX", resolved.Repository);
        Assert.Equal($"Repo.XX{Sep}2026{Sep}Data.Tax{Sep}lex.json", resolved.RelativePath);
        Assert.False(resolved.IsRoot);
    }

    [Theory]
    [InlineData("Repo.XX/")]
    [InlineData(@"Repo.XX\")]
    [InlineData("Repo.XX")]
    public void Relative_RepositoryFolder_IsValid(string path)
    {
        var resolved = workspace.ResolvePath(path);
        Assert.Equal(Path.Combine(fixture.Root, "Repo.XX"), resolved.FullPath);
        Assert.Equal("Repo.XX", resolved.RelativePath);
    }

    [Fact]
    public void Relative_MissingFile_ResolvesBelowFirstRoot()
    {
        var resolved = workspace.ResolvePath("Repo.XX/2027/Data.Tax/lex.json");
        Assert.Equal(Path.Combine(fixture.Root, "Repo.XX", "2027", "Data.Tax", "lex.json"), resolved.FullPath);
    }

    [Fact]
    public void Relative_RepositoryInSecondRoot_ResolvesBelowSecondRoot()
    {
        var resolved = workspace.ResolvePath("Repo.ZZ/file.json");
        Assert.Equal(fixture.Root2, resolved.Root);
        Assert.Equal(Path.Combine(fixture.Root2, "Repo.ZZ", "file.json"), resolved.FullPath);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData(@".\")]
    public void Relative_CurrentFolder_IsFirstRoot(string path)
    {
        var resolved = workspace.ResolvePath(path);
        Assert.True(resolved.IsRoot);
        Assert.Null(resolved.Repository);
        Assert.Equal(fixture.Root, resolved.FullPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPath_Throws(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => workspace.ResolvePath(path));
    }

    #endregion

    #region Parent segments

    [Theory]
    [InlineData("..")]
    [InlineData("../outside/secret.json")]
    [InlineData(@"..\outside\secret.json")]
    [InlineData("Repo.XX/..")]
    [InlineData("Repo.XX/../Other/secret.json")]
    [InlineData(@"Repo.XX\..\Other\secret.json")]
    [InlineData(@"Repo.XX/..\Other/secret.json")]
    [InlineData("Repo.XX/2026/../../../outside/secret.json")]
    [InlineData("Repo.XX/2026/..")]
    [InlineData("./../outside")]
    [InlineData("Repo.XX/./../Repo.YY/file.json")]
    public void ParentSegment_IsDenied(string path)
    {
        AssertDenied(path);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("Repo.XX/.../file.json")]
    [InlineData("Repo.XX/.. /file.json")]
    [InlineData("Repo.XX/ ../file.json")]
    [InlineData("Repo.XX/. ./file.json")]
    [InlineData("Repo.XX/ /file.json")]
    public void DotAndSpaceOnlySegment_IsDenied(string path)
    {
        AssertDenied(path);
    }

    [Fact]
    public void ParentSegment_InsideRepository_IsDenied()
    {
        // stays inside the workspace, but parent segments are never accepted
        AssertDenied("Repo.XX/2026/../2026/Data.Tax/lex.json");
    }

    #endregion

    #region Absolute paths

    [Fact]
    public void Absolute_InsideRoot_IsValid()
    {
        var path = Path.Combine(fixture.Root, "Repo.YY", "file.json");
        var resolved = workspace.ResolvePath(path);
        Assert.Equal(path, resolved.FullPath);
        Assert.Equal("Repo.YY", resolved.Repository);
    }

    [Fact]
    public void Absolute_InsideRoot_ForwardSlashes_IsValid()
    {
        var path = Path.Combine(fixture.Root, "Repo.YY", "file.json");
        var resolved = workspace.ResolvePath(path.Replace('\\', '/'));
        Assert.Equal(path, resolved.FullPath);
    }

    [Fact]
    public void Absolute_InsideRoot_Backslashes_IsValid()
    {
        var path = Path.Combine(fixture.Root, "Repo.YY", "file.json");
        var resolved = workspace.ResolvePath(path.Replace('/', '\\'));
        Assert.Equal(path, resolved.FullPath);
    }

    [Fact]
    public void Absolute_SecondRoot_IsValid()
    {
        var path = Path.Combine(fixture.Root2, "Repo.ZZ", "file.json");
        var resolved = workspace.ResolvePath(path);
        Assert.Equal(fixture.Root2, resolved.Root);
        Assert.Equal(path, resolved.FullPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Absolute_Root_IsRoot(bool trailingSeparator)
    {
        var path = trailingSeparator ? fixture.Root + Sep : fixture.Root;
        var resolved = workspace.ResolvePath(path);
        Assert.True(resolved.IsRoot);
        Assert.Null(resolved.Repository);
        Assert.Equal(fixture.Root, resolved.FullPath);
    }

    [Fact]
    public void Absolute_OutsideRoots_IsDenied()
    {
        AssertDenied(Path.Combine(fixture.Outside, "secret.json"));
        AssertDenied(Path.Combine(fixture.Outside, "secret.json").Replace('\\', '/'));
    }

    [Fact]
    public void Absolute_BaseFolder_IsDenied()
    {
        AssertDenied(fixture.BasePath);
    }

    [Fact]
    public void Absolute_PrefixSiblingOfRoot_IsDenied()
    {
        // 'rootX' starts with the root name 'root' but is not below it
        AssertDenied(Path.Combine(fixture.PrefixSibling, "Repo.XX", "file.json"));
    }

    [Fact]
    public void Absolute_ParentSegmentBackIntoRoot_IsDenied()
    {
        AssertDenied(Path.Combine(fixture.Root, "..", "root", "Repo.YY", "file.json"));
    }

    [Fact]
    public void Absolute_ParentSegmentOutOfRoot_IsDenied()
    {
        AssertDenied(Path.Combine(fixture.Root, "Repo.XX", "..", "..", "outside", "secret.json"));
    }

    #endregion

    #region Repository mask

    [Theory]
    [InlineData("Other/secret.json")]
    [InlineData(@"Other\secret.json")]
    [InlineData("Other")]
    [InlineData("readme.md")]
    [InlineData("Repo/file.json")]
    public void Relative_FolderNotMatchingMask_IsDenied(string path)
    {
        AssertDenied(path);
    }

    [Fact]
    public void Absolute_FolderNotMatchingMask_IsDenied()
    {
        AssertDenied(Path.Combine(fixture.Root, "Other", "secret.json"));
        AssertDenied(Path.Combine(fixture.Root, "readme.md"));
    }

    [Fact]
    public void NoMask_AnyFolder_IsValid()
    {
        var unmasked = new WorkspaceContext { Roots = [fixture.Root] };
        var resolved = unmasked.ResolvePath("Other/secret.json");
        Assert.Equal("Other", resolved.Repository);
    }

    [Fact]
    public void Mask_CaseInsensitive_OnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths are case-insensitive.");
        var resolved = workspace.ResolvePath("repo.yy/FILE.json");
        Assert.Equal("repo.yy", resolved.Repository);
    }

    [Fact]
    public void Mask_CaseSensitive_OnLinux()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Linux paths are case-sensitive.");
        AssertDenied("repo.yy/file.json");
    }

    #endregion

    #region Invalid characters

    [Theory]
    [InlineData("Repo.XX/file.json\0.txt")]
    [InlineData("Repo.XX/\tfile.json")]
    [InlineData("Repo.XX/file\n.json")]
    [InlineData("Repo.*/file.json")]
    [InlineData("Repo.XX/*.json")]
    [InlineData("Repo.X?/file.json")]
    public void ControlAndWildcardCharacters_AreDenied(string path)
    {
        AssertDenied(path);
    }

    [Theory]
    [InlineData("Repo.YY/file.json:stream")]
    [InlineData("Repo.YY/file.json::$DATA")]
    [InlineData("Repo.YY:stream/file.json")]
    [InlineData("C:Repo.YY/file.json")]
    [InlineData("C:")]
    public void Colon_InRelativePath_IsDenied(string path)
    {
        AssertDenied(path);
    }

    #endregion

    #region Windows paths

    [Theory]
    [InlineData(@"\\server\share\Repo.XX\file.json")]
    [InlineData("//server/share/Repo.XX/file.json")]
    [InlineData(@"\\?\C:\Repo.XX\file.json")]
    [InlineData(@"\\.\C:\Repo.XX\file.json")]
    [InlineData(@"\\?\UNC\server\share\Repo.XX")]
    [InlineData(@"\\.\pipe\name")]
    [InlineData(@"/\server\share")]
    [InlineData(@"\/server/share")]
    public void NetworkAndDevicePaths_AreDenied(string path)
    {
        AssertDenied(path);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData("C:/Windows/System32/drivers/etc/hosts")]
    [InlineData(@"C:\")]
    [InlineData("C:/")]
    [InlineData(@"\Windows\System32")]
    [InlineData("/Windows/System32")]
    [InlineData(@"C:Windows\System32")]
    public void Windows_SystemPaths_AreDenied(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path forms.");
        AssertDenied(path);
    }

    [Fact]
    public void Windows_RootedWithoutDrive_IsDenied()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path forms.");
        // the root path without its drive letter is rooted to the current drive
        var withoutDrive = fixture.Root[2..];
        AssertDenied(Path.Combine(withoutDrive, "Repo.YY", "file.json"));
        AssertDenied(Path.Combine(withoutDrive, "Repo.YY", "file.json").Replace('\\', '/'));
    }

    [Fact]
    public void Windows_DriveRelativeToRoot_IsDenied()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path forms.");
        // 'C:Repo.YY' is relative to the current folder of drive C
        var drive = fixture.Root[..2];
        AssertDenied(drive + @"Repo.YY\file.json");
    }

    [Fact]
    public void Windows_DifferentDriveLetterCase_IsValid()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path forms.");
        var path = Path.Combine(fixture.Root, "Repo.YY", "file.json");
        var swapped = (char.IsUpper(path[0]) ? char.ToLowerInvariant(path[0]) : char.ToUpperInvariant(path[0])) + path[1..];
        var resolved = workspace.ResolvePath(swapped);
        Assert.Equal("Repo.YY", resolved.Repository);
    }

    [Theory]
    [InlineData("Repo.XX/NUL")]
    [InlineData("Repo.XX/nul.json")]
    [InlineData("Repo.XX/CON")]
    [InlineData("Repo.XX/con.txt")]
    [InlineData("Repo.XX/PRN.log")]
    [InlineData("Repo.XX/AUX")]
    [InlineData("Repo.XX/COM1")]
    [InlineData("Repo.XX/lpt9.txt")]
    // ReSharper disable once StringLiteralTypo
    [InlineData("Repo.XX/CONIN$")]
    [InlineData("Repo.XX/NUL /file.json")]
    public void Windows_DeviceNames_AreDenied(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows device names.");
        AssertDenied(path);
    }

    [Theory]
    [InlineData("Repo.XX./file.json")]
    [InlineData("Repo.XX /file.json")]
    [InlineData("Repo.YY/file.json.")]
    [InlineData("Repo.YY/file.json. . ")]
    [InlineData("Repo.YY /file.json")]
    public void Windows_TrailingDotOrSpace_IsDenied(string path)
    {
        // Windows strips trailing dots and spaces, which would alias another folder
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows segment normalization.");
        AssertDenied(path);
    }

    [Theory]
    [InlineData("Repo.XX/2026/Data.Tax/lex.json<")]
    [InlineData("Repo.XX/2026/Data.Tax/lex.json>")]
    [InlineData("Repo.XX/2026/Data.Tax/lex|json")]
    [InlineData("Repo.XX/2026/Data.Tax/\"lex.json\"")]
    public void Windows_InvalidFileNameCharacters_AreDenied(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows file name characters.");
        AssertDenied(path);
    }

    #endregion

    #region Linux paths

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/")]
    [InlineData("//etc/passwd")]
    [InlineData("/root/.ssh/id_rsa")]
    [InlineData("/proc/self/environ")]
    [InlineData("/dev/null")]
    public void Linux_SystemPaths_AreDenied(string path)
    {
        // denied on every platform: outside the roots on Linux, rooted without drive on Windows
        AssertDenied(path);
    }

    [Theory]
    [InlineData("~/secret.json")]
    [InlineData("~")]
    public void Linux_HomeShortcut_IsNoHomeFolder(string path)
    {
        // '~' is an ordinary folder name, resolved below the root and checked against the mask
        AssertDenied(path);
    }

    [Theory]
    [InlineData(@"Repo.XX\..\Other\secret.json")]
    [InlineData(@"..\outside")]
    public void Linux_BackslashIsSeparator(string path)
    {
        // a backslash is a valid file name character on Linux, but is treated as separator
        AssertDenied(path);
    }

    [Fact]
    public void Linux_RootedPathOutsideRoot_IsDenied()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Linux path forms.");
        AssertDenied(Path.Combine(fixture.Outside, "secret.json"));
    }

    [Fact]
    public void Linux_TrailingDotFolder_IsDistinctFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Linux segment handling.");
        // on Linux 'Repo.XX.' is a distinct folder matching the mask
        var resolved = workspace.ResolvePath("Repo.XX./file.json");
        Assert.Equal("Repo.XX.", resolved.Repository);
    }

    #endregion

    #region Links

    [Fact]
    public void Link_FolderToOutside_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "link");
        WorkspaceFixture.CreateDirectoryLink(link, fixture.Outside);

        AssertDenied("Repo.XX/link");
        AssertDenied("Repo.XX/link/secret.json");
        AssertDenied(@"Repo.XX\link\secret.json");
        AssertDenied(Path.Combine(link, "secret.json"));
    }

    [Fact]
    public void Link_FolderToOutside_DeepPath_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "2026", "link");
        WorkspaceFixture.CreateDirectoryLink(link, fixture.Outside);

        AssertDenied("Repo.XX/2026/link/missing/file.json");
    }

    [Fact]
    public void Link_RepositoryToOutside_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.LINK");
        WorkspaceFixture.CreateDirectoryLink(link, fixture.Outside);

        AssertDenied("Repo.LINK/secret.json");
    }

    [Fact]
    public void Link_ToPrefixSibling_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "sibling");
        WorkspaceFixture.CreateDirectoryLink(link, fixture.PrefixSibling);

        AssertDenied("Repo.XX/sibling/Repo.XX/file.json");
    }

    [Fact]
    public void Link_ToFolderNotMatchingMask_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "other");
        WorkspaceFixture.CreateDirectoryLink(link, Path.Combine(fixture.Root, "Other"));

        AssertDenied("Repo.XX/other/secret.json");
    }

    [Fact]
    public void Link_ToRepositoryInsideRoot_IsValid()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "shared");
        WorkspaceFixture.CreateDirectoryLink(link, Path.Combine(fixture.Root, "Repo.YY"));

        var resolved = workspace.ResolvePath("Repo.XX/shared/file.json");
        Assert.Equal(Path.Combine(link, "file.json"), resolved.FullPath);
        Assert.Equal(Path.Combine(fixture.Root, "Repo.YY", "file.json"), resolved.RealPath);
        Assert.Equal("Repo.XX", resolved.Repository);
    }

    [Fact]
    public void Link_ToRepositoryInOtherRoot_IsValid()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "zz");
        WorkspaceFixture.CreateDirectoryLink(link, Path.Combine(fixture.Root2, "Repo.ZZ"));

        var resolved = workspace.ResolvePath("Repo.XX/zz/file.json");
        Assert.Equal(Path.Combine(fixture.Root2, "Repo.ZZ", "file.json"), resolved.RealPath);
    }

    [Fact]
    public void Link_Dangling_ToOutside_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "dangling");
        WorkspaceFixture.CreateDirectoryLink(link, Path.Combine(fixture.Outside, "missing"));

        AssertDenied("Repo.XX/dangling");
        AssertDenied("Repo.XX/dangling/file.json");
    }

    [Fact]
    public void Link_Chain_EndingOutside_IsDenied()
    {
        // Repo.XX/first -> Repo.YY/second -> outside
        var second = Path.Combine(fixture.Root, "Repo.YY", "second");
        WorkspaceFixture.CreateDirectoryLink(second, fixture.Outside);
        var first = Path.Combine(fixture.Root, "Repo.XX", "first");
        WorkspaceFixture.CreateDirectoryLink(first, Path.Combine(fixture.Root, "Repo.YY"));

        AssertDenied("Repo.XX/first/second/secret.json");
    }

    [Fact]
    public void Link_Cycle_IsDenied()
    {
        var a = Path.Combine(fixture.Root, "Repo.XX", "a");
        var b = Path.Combine(fixture.Root, "Repo.XX", "b");
        WorkspaceFixture.CreateDirectoryLink(a, b);
        WorkspaceFixture.CreateDirectoryLink(b, a);

        AssertDenied("Repo.XX/a/file.json");
    }

    [Fact]
    public void Link_FileToOutside_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.YY", "linked.json");
        WorkspaceFixture.CreateFileLink(link, Path.Combine(fixture.Outside, "secret.json"));

        AssertDenied("Repo.YY/linked.json");
    }

    [Fact]
    public void Link_FileInsideRepository_IsValid()
    {
        var link = Path.Combine(fixture.Root, "Repo.YY", "linked.json");
        WorkspaceFixture.CreateFileLink(link, Path.Combine(fixture.Root, "Repo.YY", "file.json"));

        var resolved = workspace.ResolvePath("Repo.YY/linked.json");
        Assert.Equal(Path.Combine(fixture.Root, "Repo.YY", "file.json"), resolved.RealPath);
    }

    [Fact]
    public void Link_RelativeTargetToOutside_IsDenied()
    {
        var link = Path.Combine(fixture.Root, "Repo.XX", "relative");
        var relativeTarget = Path.Combine("..", "..", "outside");
        try
        {
            Directory.CreateSymbolicLink(link, relativeTarget);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links cannot be created in this environment.");
        }

        AssertDenied("Repo.XX/relative/secret.json");
    }

    [Fact]
    public void Link_RootIsLink_ResolvesBelowLinkedRoot()
    {
        var linkedRoot = fixture.Combine("linked-root");
        WorkspaceFixture.CreateDirectoryLink(linkedRoot, fixture.Root);
        var linked = new WorkspaceContext { Roots = [linkedRoot], RepoMask = WorkspaceFixture.RepoMask };
        linked.Validate();

        var resolved = linked.ResolvePath("Repo.YY/file.json");
        Assert.Equal(Path.Combine(linkedRoot, "Repo.YY", "file.json"), resolved.FullPath);
        Assert.Equal(Path.Combine(fixture.Root, "Repo.YY", "file.json"), resolved.RealPath);
    }

    [Fact]
    public void Link_RootIsLink_LinkOutsideRealRoot_IsDenied()
    {
        var linkedRoot = fixture.Combine("linked-root");
        WorkspaceFixture.CreateDirectoryLink(linkedRoot, fixture.Root);
        var link = Path.Combine(fixture.Root, "Repo.XX", "escape");
        WorkspaceFixture.CreateDirectoryLink(link, fixture.Outside);
        var linked = new WorkspaceContext { Roots = [linkedRoot], RepoMask = WorkspaceFixture.RepoMask };

        Assert.Throws<UnauthorizedAccessException>(() => linked.ResolvePath("Repo.XX/escape/secret.json"));
    }

    #endregion
}
