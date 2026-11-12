using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Xunit;

namespace PayrollEngine.Mcp.Server.Tests;

/// <summary>Static check: the server runs without a console executable. No project or package of the server chain
/// references the console, and no server code starts a child process.</summary>
public class NoConsoleDependencyTest
{
    private static readonly string[] ServerAssemblies =
    [
        "PayrollEngine.Mcp.Server.dll",
        "PayrollEngine.Mcp.Tools.dll",
        "PayrollEngine.Mcp.Core.dll"
    ];

    [Fact]
    public void DependencyList_HoldsNoConsolePackage()
    {
        var depsFile = Path.Combine(AppContext.BaseDirectory, "PayrollEngine.Mcp.Server.Tests.deps.json");
        Assert.True(File.Exists(depsFile), $"Dependency list not found: {depsFile}");
        using var document = JsonDocument.Parse(File.ReadAllText(depsFile));

        var libraries = document.RootElement.GetProperty("libraries").EnumerateObject().Select(x => x.Name).ToList();

        Assert.NotEmpty(libraries);
        Assert.DoesNotContain(libraries, x => x.Contains("PayrollConsole", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(libraries, x => x.Contains("pecmd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OutputFolder_HoldsNoConsoleExecutable()
    {
        var files = Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToList();

        Assert.DoesNotContain(files, x => x!.Contains("PayrollConsole", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, x => x!.StartsWith("pecmd", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Assembly_ReferencesNoConsoleAssembly_AndStartsNoProcess(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), $"Assembly not found: {path}");

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var references = reader.AssemblyReferences
            .Select(x => reader.GetString(reader.GetAssemblyReference(x).Name))
            .ToList();
        Assert.DoesNotContain(references, x => x.Contains("PayrollConsole", StringComparison.OrdinalIgnoreCase));

        var types = reader.TypeReferences
            .Select(reader.GetTypeReference)
            .Select(x => $"{reader.GetString(x.Namespace)}.{reader.GetString(x.Name)}")
            .ToList();
        Assert.DoesNotContain("System.Diagnostics.Process", types);
        Assert.DoesNotContain("System.Diagnostics.ProcessStartInfo", types);
    }

    [Fact]
    public void ServerProject_ReferencesNoConsole_AndStartsNoProcess()
    {
        var folder = FindServerFolder();

        foreach (var project in Directory.EnumerateFiles(folder, "*.csproj"))
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("PayrollConsole", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pecmd", text, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var source in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                     .Where(x => !IsBuildOutput(x)))
        {
            var text = File.ReadAllText(source);
            Assert.False(text.Contains("pecmd", StringComparison.OrdinalIgnoreCase), $"pecmd in {source}");
            Assert.False(text.Contains("Process.Start", StringComparison.Ordinal), $"Process.Start in {source}");
            Assert.False(text.Contains("ProcessStartInfo", StringComparison.Ordinal), $"ProcessStartInfo in {source}");
        }
    }

    public static IEnumerable<object[]> Assemblies() => ServerAssemblies.Select(x => new object[] { x });

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string ThisFile([CallerFilePath] string path = "") => path;

    /// <summary>The folder of the server project, found above this source file</summary>
    private static string FindServerFolder()
    {
        // the source folder of this file: also valid when the tests are built into another output folder
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(ThisFile())!); folder != null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "Server");
            if (File.Exists(Path.Combine(candidate, "PayrollEngine.Mcp.Server.csproj")))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("The folder of the server project was not found above the test sources.");
    }
}
