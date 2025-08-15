using System;
using System.Diagnostics;
using System.IO;
using Xunit;

public class ConfigTests
{
    [Fact]
    public void Config_Init_Creates_File()
    {
        var (exe, repo) = PrepareRepo();
        var output = RunCli(exe, repo, "config init");
        Assert.True(File.Exists(Path.Combine(repo, "cd-index.toml")));
        var text = File.ReadAllText(Path.Combine(repo, "cd-index.toml"));
        Assert.Contains("[scan]", text);
        Assert.Contains("[commands]", text);
    }

    [Fact]
    public void Example_Config_Has_Rich_Sections()
    {
        var example = CdIndex.Cli.Config.ConfigExampleBuilder.Build(CdIndex.Cli.Config.ScanConfig.Defaults());
        Assert.Contains("[scan]", example);
        Assert.Contains("[tree]", example);
        Assert.Contains("[di]", example);
        Assert.Contains("[commands]", example);
        Assert.Contains("[flow]", example);
        Assert.Contains("Callgraph extraction", example); // commented callgraph guidance
        // ensure comments present
        Assert.Contains("# cd-index configuration", example);
        Assert.Contains("# sections:", example);
    }

    [Fact]
    public void Config_Print_Defaults_When_No_File()
    {
        var (exe, repo) = PrepareRepo();
        var printed = RunCli(exe, repo, "config print");
        Assert.Contains("[scan]", printed);
        Assert.Contains("CFG001", printed); // diagnostic to stderr
    }

    [Fact]
    public void Scan_Uses_Toml_Config_For_Ignore()
    {
        var (exe, repo) = PrepareRepo();
        File.WriteAllText(Path.Combine(repo, "cd-index.toml"), "[scan]\nignore=[\"foo\"]\n[tree]\nlocMode=\"physical\"\nuseGitignore=false\n");
        Directory.CreateDirectory(Path.Combine(repo, "foo"));
        File.WriteAllText(Path.Combine(repo, "foo", "x.cs"), "// x\n");
        File.WriteAllText(Path.Combine(repo, "keep.cs"), "// keep\n");
        var proj = CreateDummyProj(repo);
        var json = RunCli(exe, repo, $"scan --csproj {proj}");
        Assert.DoesNotContain("foo/x.cs", json);
        Assert.Contains("keep.cs", json);
    }

    private static (string exe, string repo) PrepareRepo()
    {
        var exe = FindCliExe();
        var repo = Path.Combine(Path.GetTempPath(), "cfg-test-" + Guid.NewGuid());
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
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return stdout + stderr; // merge for assertions
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
