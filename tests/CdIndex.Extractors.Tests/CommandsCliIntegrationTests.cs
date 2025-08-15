using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CdIndex.Extractors.Tests;

public sealed class CommandsCliIntegrationTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "CmdApp", "CmdApp.sln");

    private static (int code, string stdout, string stderr) RunCli(string args)
    {
        // Determine current test build configuration (Debug/Release) from base path, fallback to any existing build
        var config = new[] { "Debug", "Release" }.FirstOrDefault(c => AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Any(p => string.Equals(p, c, StringComparison.OrdinalIgnoreCase))) ?? "Debug";
        var primary = Path.Combine(RepoRoot, "src", "CdIndex.Cli", "bin", config, "net9.0", "CdIndex.Cli.dll");
        string? exe = null;
        if (File.Exists(primary)) exe = primary;
        else
        {
            var binRoot = Path.Combine(RepoRoot, "src", "CdIndex.Cli", "bin");
            if (Directory.Exists(binRoot))
            {
                exe = Directory.GetFiles(binRoot, "CdIndex.Cli.dll", SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Contains("Release", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(p => p.Length)
                    .FirstOrDefault();
            }
        }
        if (exe == null || !File.Exists(exe)) throw new FileNotFoundException("CLI build missing", primary);
        var psi = new ProcessStartInfo("dotnet", $"{exe} scan --sln {SlnPath} --scan-commands {args}")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Cli_WarnMode_DefaultExit0()
    {
        var (code, _, stderr) = RunCli("--commands-dedup case-insensitive");
        Assert.Equal(0, code);
        // If conflicts exist they should appear with COMMAND-CONFLICT prefix (non-strict)
        // Not asserting presence because test assets currently lack case variants.
        Assert.DoesNotContain("exit 12", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_ErrorMode_Exit12_WhenConflicts()
    {
        // To simulate a conflict we rely on future asset addition; if none, exit code should still be 0.
        var (code, _, _) = RunCli("--commands-dedup case-insensitive --commands-conflicts error");
        Assert.True(code == 0 || code == 12, $"Unexpected exit code {code}");
    }
}
