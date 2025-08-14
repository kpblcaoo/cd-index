using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

public class ScanCommandsRouterNamesTests
{
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

    private static string CliDll() => Path.Combine(RepoRoot(), "src", "CdIndex.Cli", "bin", "Debug", "net9.0", "CdIndex.Cli.dll");

    private static JsonDocument Run(string args)
    {
        var exe = CliDll();
        if (!File.Exists(exe)) throw new FileNotFoundException(exe);
        var psi = new ProcessStartInfo("dotnet", exe + " " + args)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"ExitCode={p.ExitCode} stderr={err}");
        Assert.False(string.IsNullOrWhiteSpace(output));
        return JsonDocument.Parse(output);
    }

    [Fact]
    public void Scan_CommandsSection_CustomRouterNames_Filters()
    {
        var root = RepoRoot();
        var sln = Path.Combine(root, "tests", "CdIndex.Extractors.Tests", "TestAssets", "CmdApp", "CmdApp.sln");
        Assert.True(File.Exists(sln));
        // Only include Register; Map/Add should be excluded
        using var doc = Run($"scan --sln {sln} --scan-commands --commands-router-names Register");
        var rootEl = doc.RootElement;
        var commands = rootEl.GetProperty("Commands");
        Assert.Equal(JsonValueKind.Array, commands.ValueKind);
        Assert.True(commands.GetArrayLength() > 0);
        var items = commands[0].GetProperty("Items");
        var commandTexts = items.EnumerateArray().Select(i => i.GetProperty("Command").GetString()).ToList();
        Assert.Contains("/stats", commandTexts);
        Assert.DoesNotContain("/start", commandTexts); // filtered (Map)
        Assert.DoesNotContain("/ping", commandTexts);  // filtered (Add)
    }
}
