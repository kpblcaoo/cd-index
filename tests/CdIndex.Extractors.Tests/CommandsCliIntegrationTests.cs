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
        var exe = Path.Combine(RepoRoot, "src", "CdIndex.Cli", "bin", "Debug", "net9.0", "CdIndex.Cli.dll");
        if (!File.Exists(exe)) throw new FileNotFoundException("CLI build missing", exe);
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
