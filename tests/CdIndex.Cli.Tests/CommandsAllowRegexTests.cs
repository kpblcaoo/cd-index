using System;
using System.Diagnostics;
using System.IO;
using Xunit;

public class CommandsAllowRegexTests
{
    [Fact]
    public void AllowRegex_From_Config_Overrides_Default()
    {
        var (exe, repo) = PrepareRepo();
        // Config overrides allowRegex to permit only 3 uppercase letters after slash
        File.WriteAllText(Path.Combine(repo, "cd-index.toml"), "[commands]\nallowRegex = \"^/[A-Z]{3}$\"\n");
        var proj = CreateDummyProj(repo);
    File.WriteAllText(Path.Combine(repo, "Handlers.cs"), @"public class H1{} public class H2{} class Startup { void M(){ Map(""/ABC"", new H1()); Map(""/abc"", new H2()); } void Map(string c, object h){} void Map(string c){} } ");
        var result = RunCli(exe, repo, $"scan --csproj {proj} --scan-commands --commands-include router");
        // Uppercase command should appear, lowercase filtered out by custom regex
        Assert.Contains("/ABC", result.output);
        Assert.DoesNotContain("/abc\"", result.output); // full json string token check
    }

    private static (string exe, string repo) PrepareRepo()
    {
        var exe = FindCliExe();
        var repo = Path.Combine(Path.GetTempPath(), "cmd-allowregex-test-" + Guid.NewGuid());
        Directory.CreateDirectory(repo);
        return (exe, repo);
    }

    private static string CreateDummyProj(string repo)
    {
        var projPath = Path.Combine(repo, "App.csproj");
        File.WriteAllText(projPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        return projPath;
    }

    private static (int code, string output) RunCli(string exe, string workingDir, string args)
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
        return (proc.ExitCode, stdout + stderr);
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
