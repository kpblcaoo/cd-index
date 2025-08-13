using System;
using System.Diagnostics;
using System.IO;
using Xunit;

public class SelfCheckDeterminismTests
{
    [Fact]
    public void SelfCheck_Output_IsDeterministic()
    {
        var exe = FindCliExe();
        var tempDir = CreateTestRepo();
        var output1 = RunCli(exe, tempDir, "--selfcheck");
        var output2 = RunCli(exe, tempDir, "--selfcheck");
        Assert.Equal(output1, output2); // байт-в-байт
    }

    [Fact]
    public void SelfCheck_ScanTreeOnly_OnlyTreeSectionFilled()
    {
        var exe = FindCliExe();
        var tempDir = CreateTestRepo();
        var output = RunCli(exe, tempDir, "--selfcheck --scan-tree-only");
        Assert.Contains("\"Tree\":", output);
        Assert.DoesNotContain("\"Project\": [", output);
        Assert.DoesNotContain("\"DI\": [", output);
        Assert.DoesNotContain("\"Entrypoints\": [", output);
        Assert.DoesNotContain("\"MessageFlow\": [", output);
        Assert.DoesNotContain("\"Callgraphs\": [", output);
        Assert.DoesNotContain("\"Configs\": [", output);
        Assert.DoesNotContain("\"Commands\": [", output);
        Assert.DoesNotContain("\"Tests\": [", output);
    }

    [Fact]
    public void TreePaths_AreRepoRelativeAndNormalized()
    {
        var exe = FindCliExe();
        var tempDir = CreateTestRepo();
        var output = RunCli(exe, tempDir, "--selfcheck");
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("\"Path\":"))
            {
                var path = ExtractPath(line);
                Assert.DoesNotContain("\\", path); // нет обратных слэшей
                Assert.False(Path.IsPathRooted(path)); // repo-relative
                Assert.True(path.Contains("/")); // есть прямой слэш
            }
        }
    }

    private static string FindCliExe()
    {
        // Предполагаем, что сборка уже выполнена
        var cliPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "CdIndex.Cli", "bin", "Debug", "net9.0", "CdIndex.Cli.dll");
        if (!File.Exists(cliPath)) throw new FileNotFoundException(cliPath);
        return cliPath;
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
        using var proc = Process.Start(psi);
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }

    private static string CreateTestRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "cli-selfcheck-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "a.cs"), "// test\nline2\nline3\n");
        File.WriteAllText(Path.Combine(root, "src", "b.cs"), "// test\r\nline2\r\nline3\r\n");
        File.WriteAllText(Path.Combine(root, "src", "app.sln"), "sln\n");
        File.WriteAllText(Path.Combine(root, "src", "proj.csproj"), "csproj\n");
        File.WriteAllText(Path.Combine(root, "src", "feat.feature"), "feature\n");
        File.WriteAllText(Path.Combine(root, "src", "conf.yaml"), "yaml\n");
        File.WriteAllText(Path.Combine(root, "src", "data.json"), "json\n");
        return root;
    }

    private static string ExtractPath(string line)
    {
        var idx = line.IndexOf(":");
        if (idx < 0) return "";
        var val = line.Substring(idx + 1).Trim().Trim('"', ',');
        return val;
    }
}
