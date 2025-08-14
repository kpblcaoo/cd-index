using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;

public class ScanCommandTests
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

    private static string Cli()
        => Path.Combine(RepoRoot(), "src", "CdIndex.Cli", "bin", "Debug", "net9.0", "CdIndex.Cli.dll");

    [Fact]
    public void Scan_Sln_GeneratesValidSchemaJson()
    {
        var cli = Cli();
        Assert.True(File.Exists(cli), "CLI binary not built");
    var sln = Path.Combine(RepoRoot(), "tests", "CdIndex.Roslyn.Tests", "TestAssets", "MiniHostApp.sln");
        Assert.True(File.Exists(sln));
        var tempOut = Path.Combine(Path.GetTempPath(), ".project_index.json");
        if (File.Exists(tempOut)) File.Delete(tempOut);

        var psi = new ProcessStartInfo("dotnet", $"{cli} scan --sln {sln} --out {tempOut}")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.True(File.Exists(tempOut));
        var json = File.ReadAllText(tempOut);
    var schemaPath = Path.Combine(RepoRoot(), "schema", "project_index.schema.json");
        var schema = JSchema.Parse(File.ReadAllText(schemaPath));
        var obj = JObject.Parse(json);
    var valid = obj.IsValid(schema, out IList<string> errors);
    Assert.True(valid, string.Join(";", errors));
        Assert.True(obj["Tree"]?[0]?["Files"]?.HasValues == true);
    }

    [Fact]
    public void Scan_Csproj_Works()
    {
        var cli = Cli();
        var csproj = Path.Combine(RepoRoot(), "tests", "CdIndex.Roslyn.Tests", "TestAssets", "MiniHostApp", "MiniHostApp.csproj");
        Assert.True(File.Exists(csproj));
        var tempOut = Path.Combine(Path.GetTempPath(), ".project_index_csproj.json");
        if (File.Exists(tempOut)) File.Delete(tempOut);
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"{cli} scan --csproj {csproj} --out {tempOut}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.True(File.Exists(tempOut));
        var json = File.ReadAllText(tempOut);
        Assert.Contains("\"Project\":", json);
    }

    [Fact]
    public void Scan_Error_WhenBothSlnAndCsproj()
    {
        var cli = Cli();
        var sln = Path.Combine(RepoRoot(), "tests", "CdIndex.Roslyn.Tests", "TestAssets", "MiniHostApp.sln");
        var csproj = Path.Combine(RepoRoot(), "tests", "CdIndex.Roslyn.Tests", "TestAssets", "MiniHostApp", "MiniHostApp.csproj");
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"{cli} scan --sln {sln} --csproj {csproj}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.NotEqual(0, p.ExitCode);
    }

    [Fact]
    public void Scan_Error_WhenFileMissing()
    {
        var cli = Cli();
        var missing = Path.Combine(RepoRoot(), "tests", "CdIndex.Roslyn.Tests", "TestAssets", "NOPE.sln");
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"{cli} scan --sln {missing}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.NotEqual(0, p.ExitCode);
    }

    [Fact]
    public void Scan_LocMode_Logical_ReducesLoc()
    {
        var cli = Cli();
        var sln = Path.Combine(RepoRoot(), "tests", "CdIndex.Roslyn.Tests", "TestAssets", "MiniHostApp.sln");
        var tmpPhys = Path.Combine(Path.GetTempPath(), ".project_index_phys.json");
        var tmpLogical = Path.Combine(Path.GetTempPath(), ".project_index_logical.json");
        if (File.Exists(tmpPhys)) File.Delete(tmpPhys);
        if (File.Exists(tmpLogical)) File.Delete(tmpLogical);
        Process.Start(new ProcessStartInfo("dotnet", $"{cli} scan --sln {sln} --out {tmpPhys}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!.WaitForExit();
        Process.Start(new ProcessStartInfo("dotnet", $"{cli} scan --sln {sln} --loc-mode logical --out {tmpLogical}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!.WaitForExit();
        var phys = JObject.Parse(File.ReadAllText(tmpPhys));
        var logical = JObject.Parse(File.ReadAllText(tmpLogical));
        int SumLoc(JObject root) => root["Tree"]![0]!["Files"]!.Sum(f => (int)f!["Loc"]!);
        var physLoc = SumLoc(phys);
        var logicalLoc = SumLoc(logical);
        Assert.True(logicalLoc <= physLoc);
        Assert.True(physLoc > 0);
    }

    [Fact]
    public void Help_Root_ShowsCommands()
    {
        var cli = Cli();
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"{cli} --help") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Contains("Commands:", text);
        Assert.Contains("scan", text);
    }

    [Fact]
    public void Help_Scan_ShowsUsage()
    {
        var cli = Cli();
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"{cli} scan --help") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Contains("Usage: cd-index scan", text);
        Assert.Contains("--loc-mode", text);
    }
}
