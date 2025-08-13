using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using CdIndex.Core;

public class TreeScannerTests
{
    [Fact]
    public void Scan_FiltersAndNormalizes_Correctly()
    {
        var tempDir = CreateTestDir();
        try
        {
            var files = TreeScanner.Scan(tempDir);
            var paths = files.Select(f => f.Path).ToList();
            // Проверяем игноры
            Assert.DoesNotContain("bin/a.cs", paths);
            Assert.DoesNotContain("obj/b.cs", paths);
            Assert.DoesNotContain(".git/config", paths);
            Assert.DoesNotContain("logs/log.txt", paths);
            Assert.DoesNotContain("src/skip.Designer.cs", paths);
            // Проверяем включения
            Assert.Contains("src/a.cs", paths);
            Assert.Contains("src/b.cs", paths);
            Assert.Contains("src/app.sln", paths);
            Assert.Contains("src/proj.csproj", paths);
            Assert.Contains("src/feat.feature", paths);
            Assert.Contains("src/conf.yaml", paths);
            Assert.Contains("src/data.json", paths);
            // Проверяем kind
            Assert.Equal("cs", files.First(f => f.Path == "src/a.cs").Kind);
            Assert.Equal("csproj", files.First(f => f.Path == "src/proj.csproj").Kind);
            Assert.Equal("sln", files.First(f => f.Path == "src/app.sln").Kind);
            Assert.Equal("feature", files.First(f => f.Path == "src/feat.feature").Kind);
            Assert.Equal("yaml", files.First(f => f.Path == "src/conf.yaml").Kind);
            Assert.Equal("json", files.First(f => f.Path == "src/data.json").Kind);
            // Проверяем loc
            Assert.Equal(3, files.First(f => f.Path == "src/a.cs").Loc);
            Assert.Equal(3, files.First(f => f.Path == "src/b.cs").Loc);
            // Проверяем sha256 одинаков для LF/CRLF
            var shaA = files.First(f => f.Path == "src/a.cs").Sha256;
            var shaB = files.First(f => f.Path == "src/b.cs").Sha256;
            Assert.Equal(shaA, shaB);
            // Проверяем сортировку
            var sorted = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(f => f.Path).ToList();
            Assert.Equal(sorted, paths);
            Assert.Equal(7, files.Count); // Ожидается 7 файлов согласно тестовым данным
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTestDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "tree-scan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "bin", "a.cs"), "// bin file\nline2\nline3\n");
        File.WriteAllText(Path.Combine(root, "obj", "b.cs"), "// obj file\r\nline2\r\nline3\r\n");
        File.WriteAllText(Path.Combine(root, ".git", "config"), "git config\n");
        File.WriteAllText(Path.Combine(root, "logs", "log.txt"), "log\n");
        File.WriteAllText(Path.Combine(root, "src", "a.cs"), "// test\nline2\nline3\n"); // LF
        File.WriteAllText(Path.Combine(root, "src", "b.cs"), "// test\r\nline2\r\nline3\r\n"); // CRLF
        File.WriteAllText(Path.Combine(root, "src", "skip.Designer.cs"), "designer\n");
        File.WriteAllText(Path.Combine(root, "src", "app.sln"), "sln\n");
        File.WriteAllText(Path.Combine(root, "src", "proj.csproj"), "csproj\n");
        File.WriteAllText(Path.Combine(root, "src", "feat.feature"), "feature\n");
        File.WriteAllText(Path.Combine(root, "src", "conf.yaml"), "yaml\n");
        File.WriteAllText(Path.Combine(root, "src", "data.json"), "json\n");
        return root;
    }
}
