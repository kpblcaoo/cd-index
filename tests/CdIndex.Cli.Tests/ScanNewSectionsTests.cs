using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

public class ScanNewSectionsTests
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

    private static JsonNode Run(string args)
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
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(output));
        return JsonNode.Parse(output)!;
    }

    [Fact]
    public void Scan_ConfigsSection_PopulatedAndSorted()
    {
        var root = RepoRoot();
        var sln = Path.Combine(root, "tests", "CdIndex.Extractors.Tests", "TestAssets", "ConfApp", "ConfApp.sln");
        var json = Run($"scan --sln {sln} --scan-configs");
        var configs = json["Configs"]!.AsArray();
        Assert.NotEmpty(configs);
        var first = configs[0]!;
        var envKeys = first["EnvKeys"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        var appProps = first["AppProps"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("DOORMAN_BOT_API", envKeys);
        Assert.True(envKeys.SequenceEqual(envKeys.OrderBy(x => x, StringComparer.Ordinal)));
        Assert.Contains("IAppConfig.AdminChatId", appProps);
    }

    [Fact]
    public void Scan_CommandsSection_Populated()
    {
        var root = RepoRoot();
        var sln = Path.Combine(root, "tests", "CdIndex.Extractors.Tests", "TestAssets", "CmdApp", "CmdApp.sln");
        var json = Run($"scan --sln {sln} --scan-commands");
        var commands = json["Commands"]!.AsArray();
        Assert.NotEmpty(commands);
        var firstSection = commands[0]!;
        var items = firstSection["Items"]!.AsArray();
        Assert.Contains(items, i => i!["Command"]!.GetValue<string>() == "/start");
        Assert.Contains(items, i => i!["Command"]!.GetValue<string>() == "/stats");
    }

    [Fact]
    public void Scan_MessageFlowSection_Sequence()
    {
        var root = RepoRoot();
        var sln = Path.Combine(root, "tests", "CdIndex.Extractors.Tests", "TestAssets", "FlowApp", "FlowApp.sln");
        var json = Run($"scan --sln {sln} --scan-flow --flow-handler MessageHandler");
        var flow = json["MessageFlow"]!.AsArray();
        Assert.NotEmpty(flow);
        var section = flow[0]!;
        var nodes = section["Nodes"]!.AsArray();
        Assert.True(nodes.Count >= 6);
        string[] expectedKinds = { "guard", "guard", "delegate", "guard", "delegate", "delegate" };
        for (int i = 0; i < expectedKinds.Length; i++)
        {
            Assert.Equal(expectedKinds[i], nodes[i]!["Kind"]!.GetValue<string>());
            Assert.Equal(i, nodes[i]!["Order"]!.GetValue<int>());
        }
    }
}
