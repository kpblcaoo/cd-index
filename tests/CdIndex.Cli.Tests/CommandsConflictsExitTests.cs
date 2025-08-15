using System;
using System.Diagnostics;
using System.IO;
using Xunit;

public class CommandsConflictsExitTests
{
    [Fact]
    public void Conflicts_Error_ExitCode12()
    {
        var (exe, repo) = PrepareRepo();
        // create two handlers causing case-insensitive conflict when enabled
        var proj = CreateDummyProj(repo);
        File.WriteAllText(Path.Combine(repo, "HandlerA.cs"), "public class HandlerA { public const string Cmd=\"/Stats\"; }\n");
        File.WriteAllText(Path.Combine(repo, "HandlerB.cs"), "public class HandlerB { public const string Cmd=\"/stats\"; }\n");
        var result = RunCliWithExit(exe, repo, $"scan --csproj {proj} --scan-commands --commands-include router,attributes,comparison --commands-dedup case-insensitive --commands-conflicts error");
        Assert.Equal(12, result.code);
        Assert.Contains("CMD300", result.output); // conflict diagnostic
    }

    private static (string exe, string repo) PrepareRepo()
    {
        var exe = FindCliExe();
        var repo = Path.Combine(Path.GetTempPath(), "cmd-conflict-test-" + Guid.NewGuid());
        Directory.CreateDirectory(repo);
        return (exe, repo);
    }

    private static string CreateDummyProj(string repo)
    {
        var projPath = Path.Combine(repo, "App.csproj");
        File.WriteAllText(projPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        return projPath;
    }

    private static (int code, string output) RunCliWithExit(string exe, string workingDir, string args)
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
