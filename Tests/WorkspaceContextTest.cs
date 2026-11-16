using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using PayrollEngine.Mcp.Core.Isolation;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Unit tests for WorkspaceContext configuration and startup validation.</summary>
public sealed class WorkspaceContextTest : IDisposable
{
    private readonly WorkspaceFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private static IConfiguration Configuration(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    #region Configuration

    [Fact]
    public void FromConfiguration_SectionAbsent_IsEmpty()
    {
        var workspace = WorkspaceContext.FromConfiguration(Configuration([]));

        Assert.False(workspace.IsConfigured);
        Assert.Empty(workspace.Roots);
        Assert.Null(workspace.RepoMask);
        Assert.Empty(workspace.AgentIdentities);
        Assert.Equal(McpPermission.None, workspace.Backend);
        workspace.Validate(); // must not throw
    }

    [Fact]
    public void FromConfiguration_AgentIdentities_AreReadAsArrayAndAsString()
    {
        var array = WorkspaceContext.FromConfiguration(Configuration(new()
        {
            ["McpServer:Regulator:Roots:0"] = fixture.Root,
            ["McpServer:Regulator:AgentIdentities:0"] = " Agent One ",
            ["McpServer:Regulator:AgentIdentities:1"] = "Agent Two"
        }));
        var text = WorkspaceContext.FromConfiguration(Configuration(new()
        {
            ["McpServer:Regulator:Roots:0"] = fixture.Root,
            ["McpServer:Regulator:AgentIdentities"] = "Agent One; Agent Two;"
        }));

        Assert.Equal(["Agent One", "Agent Two"], array.AgentIdentities);
        Assert.Equal(["Agent One", "Agent Two"], text.AgentIdentities);
        array.Validate(); // must not throw
    }

    [Fact]
    public void Validate_AgentIdentitiesWithoutRoots_Throws()
    {
        Assert.Throws<InvalidOperationException>(new WorkspaceContext { AgentIdentities = ["Agent One"] }.Validate);
    }

    [Fact]
    public void Validate_EmptyAgentIdentity_Throws()
    {
        var workspace = new WorkspaceContext { Roots = [fixture.Root], AgentIdentities = ["Agent One", " "] };

        Assert.Throws<InvalidOperationException>(workspace.Validate);
    }

    [Fact]
    public void FromConfiguration_AllSettings_AreRead()
    {
        var workspace = WorkspaceContext.FromConfiguration(Configuration(new()
        {
            ["McpServer:Regulator:Roots:0"] = fixture.Root,
            ["McpServer:Regulator:Roots:1"] = fixture.Root2,
            ["McpServer:Regulator:RepoMask"] = "Repo.*",
            ["McpServer:Regulator:AgentIdentities:0"] = "Agent One",
            ["McpServer:Regulator:Backend"] = "read"
        }));

        Assert.Equal([fixture.Root, fixture.Root2], workspace.Roots);
        Assert.Equal("Repo.*", workspace.RepoMask);
        Assert.Equal(["Agent One"], workspace.AgentIdentities);
        Assert.Equal(McpPermission.Read, workspace.Backend);
        workspace.Validate(); // must not throw
    }

    [Fact]
    public void FromConfiguration_RemovedSettings_AreIgnored()
    {
        // Identity and Tenants are not settings of the workspace: a configuration that still has them starts as before
        var workspace = WorkspaceContext.FromConfiguration(Configuration(new()
        {
            ["McpServer:Regulator:Roots:0"] = fixture.Root,
            ["McpServer:Regulator:Identity"] = "Jane Doe",
            ["McpServer:Regulator:Tenants:0"] = "XX.Dev"
        }));

        Assert.Equal([fixture.Root], workspace.Roots);
        workspace.Validate(); // must not throw
    }

    [Fact]
    public void FromConfiguration_SemicolonSeparatedLists_AreRead()
    {
        var workspace = WorkspaceContext.FromConfiguration(Configuration(new()
        {
            ["McpServer:Regulator:Roots"] = $"{fixture.Root}; {fixture.Root2};",
            ["McpServer:Regulator:AgentIdentities"] = "Agent One;Agent Two"
        }));

        Assert.Equal([fixture.Root, fixture.Root2], workspace.Roots);
        Assert.Equal(["Agent One", "Agent Two"], workspace.AgentIdentities);
    }

    [Theory]
    [InlineData("Full")]
    [InlineData("readonly")]
    [InlineData("7")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("Read,Write")]
    [InlineData("None,Read")]
    [InlineData("Write")]
    [InlineData(" write ")]
    public void FromConfiguration_InvalidBackend_Throws(string backend)
    {
        var configuration = Configuration(new()
        {
            ["McpServer:Regulator:Roots:0"] = fixture.Root,
            ["McpServer:Regulator:Backend"] = backend
        });
        var exception = Assert.Throws<InvalidOperationException>(
            () => WorkspaceContext.FromConfiguration(configuration));
        Assert.Contains("allowed: None, Read)", exception.Message);
    }

    [Fact]
    public void Validate_BackendWrite_Throws()
    {
        var workspace = new WorkspaceContext { Roots = [fixture.Root], Backend = McpPermission.Write };

        var exception = Assert.Throws<InvalidOperationException>(workspace.Validate);
        Assert.Contains("allowed: None, Read)", exception.Message);
    }

    [Theory]
    [InlineData("read", McpPermission.Read)]
    [InlineData(" Read ", McpPermission.Read)]
    [InlineData("", McpPermission.None)]
    [InlineData("  ", McpPermission.None)]
    [InlineData("None", McpPermission.None)]
    public void FromConfiguration_Backend_IgnoresCaseAndBlanks(string backend, McpPermission expected)
    {
        var workspace = WorkspaceContext.FromConfiguration(Configuration(new()
        {
            ["McpServer:Regulator:Roots:0"] = fixture.Root,
            ["McpServer:Regulator:Backend"] = backend
        }));
        Assert.Equal(expected, workspace.Backend);
    }

    #endregion

    #region Validation

    [Fact]
    public void Validate_SettingsWithoutRoots_Throws()
    {
        Assert.Throws<InvalidOperationException>(new WorkspaceContext { RepoMask = "Repo.*" }.Validate);
        Assert.Throws<InvalidOperationException>(new WorkspaceContext { Backend = McpPermission.Read }.Validate);
    }

    [Fact]
    public void Validate_RootForwardSlashes_IsValid()
    {
        new WorkspaceContext { Roots = [fixture.Root.Replace('\\', '/')] }.Validate(); // must not throw
    }

    [Fact]
    public void Validate_RootTrailingSeparator_IsValid()
    {
        var workspace = new WorkspaceContext { Roots = [fixture.Root + Path.DirectorySeparatorChar] };
        workspace.Validate();
        Assert.Equal(fixture.Root, workspace.ResolvePath(".").FullPath);
    }

    [Fact]
    public void Validate_RootMissing_Throws()
    {
        var workspace = new WorkspaceContext { Roots = [fixture.Combine("missing")] };
        Assert.Throws<InvalidOperationException>(workspace.Validate);
    }

    [Theory]
    [InlineData("relative/root")]
    [InlineData("./root")]
    [InlineData("   ")]
    public void Validate_RootNotFullyQualified_Throws(string root)
    {
        var workspace = new WorkspaceContext { Roots = [root] };
        Assert.Throws<InvalidOperationException>(workspace.Validate);
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData(@"\\?\C:\Repos")]
    public void Validate_RootNetworkOrDevicePath_Throws(string root)
    {
        var workspace = new WorkspaceContext { Roots = [root] };
        Assert.Throws<InvalidOperationException>(workspace.Validate);
    }

    [Fact]
    public void Validate_RootIsFile_Throws()
    {
        var workspace = new WorkspaceContext { Roots = [Path.Combine(fixture.Root, "readme.md")] };
        Assert.Throws<InvalidOperationException>(workspace.Validate);
    }

    [Theory]
    [InlineData("Repo.*/sub")]
    [InlineData(@"Repo\*")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:Repo.*")]
    public void Validate_InvalidRepoMask_Throws(string repoMask)
    {
        var workspace = new WorkspaceContext { Roots = [fixture.Root], RepoMask = repoMask };
        Assert.Throws<InvalidOperationException>(workspace.Validate);
    }

    [Fact]
    public void ResolvePath_NoRoots_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new WorkspaceContext().ResolvePath("Repo.XX"));
    }

    #endregion

    #region Repository mask

    [Theory]
    [InlineData("Repo.XX", true)]
    [InlineData("Repo.", true)]
    [InlineData("Repo", false)]
    [InlineData("Other", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRepositoryName_MatchesMask(string name, bool expected)
    {
        var workspace = new WorkspaceContext { Roots = [fixture.Root], RepoMask = "Repo.*" };
        Assert.Equal(expected, workspace.IsRepositoryName(name));
    }

    #endregion
}
