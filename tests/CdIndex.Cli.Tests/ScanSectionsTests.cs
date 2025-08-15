using System;
using System.Diagnostics;
using System.IO;
using Xunit;

public class ScanSectionsTests
{
    [Fact]
    public void Scan_NoTree_Shorthand_OmitsTree()
    {
        var (exe, repo) = PrepareRepo();
        var output = RunCli(exe, repo, $"scan --csproj {CreateDummyProj(repo)} --no-tree");
        Assert.DoesNotContain("\"Tree\":", output);
        Assert.Contains("\"DI\":", output); // empty array present
    }

    [Fact]
    public void Scan_Sections_List_SelectsSubset()
    {
        var (exe, repo) = PrepareRepo();
        var proj = CreateDummyProj(repo);
        var output = RunCli(exe, repo, $"scan --csproj {proj} --sections DI,Entrypoints");
        Assert.Contains("\"DI\":", output);
        Assert.Contains("\"Entrypoints\":", output);
        Assert.DoesNotContain("\"Tree\":", output);
        Assert.DoesNotContain("\"Configs\":", output);
    }

    [Fact]
    public void Scan_CommaSeparated_Ext_And_Ignore()
    {
        var (exe, repo) = PrepareRepo();
        File.WriteAllText(Path.Combine(repo, "a.cs"), "// a\n");
        File.WriteAllText(Path.Combine(repo, "b.csproj"), "<Project></Project>");
        File.WriteAllText(Path.Combine(repo, "c.json"), "{}\n");
        var proj = CreateDummyProj(repo);
        var output = RunCli(exe, repo, $"scan --csproj {proj} --ext .cs,.json --ignore bin,obj,tmp");
        Assert.Contains("a.cs", output);
        Assert.Contains("c.json", output);
    }

    [Fact]
    public void Scan_Gitignore_Filters()
    {
        var (exe, repo) = PrepareRepo();
        Directory.CreateDirectory(Path.Combine(repo, "bin"));
        File.WriteAllText(Path.Combine(repo, "bin", "x.cs"), "// bin\n");
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "bin/\nStrykerOutput/\n");
        File.WriteAllText(Path.Combine(repo, "keep.cs"), "// keep\n");
        var proj = CreateDummyProj(repo);
        var output = RunCli(exe, repo, $"scan --csproj {proj} --gitignore");
        Assert.DoesNotContain("bin/x.cs", output);
        Assert.Contains("keep.cs", output);
    }

    private static (string exe, string repo) PrepareRepo()
    {
        var exe = FindCliExe();
        var repo = Path.Combine(Path.GetTempPath(), "scan-sections-test-" + Guid.NewGuid());
        Directory.CreateDirectory(repo);
        return (exe, repo);
    }

    private static string CreateDummyProj(string repo)
    {
        var projPath = Path.Combine(repo, "App.csproj");
        File.WriteAllText(projPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        return projPath;
    }

    private static string RunCli(string exe, string workingDir, string args)
    {
        var psi = new ProcessStartInfo("dotnet", $"{exe} {args}")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "cd-index.sln"))) return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        throw new InvalidOperationException("Repo root not found");
    }

    private static string FindCliExe()
    {
        var cliPath = Path.Combine(RepoRoot(), "src", "CdIndex.Cli", "bin", "Debug", "net9.0", "CdIndex.Cli.dll");
        if (!File.Exists(cliPath)) throw new FileNotFoundException(cliPath);
        return cliPath;
    }
}
