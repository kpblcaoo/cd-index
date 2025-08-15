using System;
using System.IO;
using System.Linq;
using Xunit;
using CdIndex.Core;

public class GitignoreMergeTests
{
    [Fact]
    public void Gitignore_SimplePatterns_Filtered()
    {
        var root = Path.Combine(Path.GetTempPath(), "gitignore-merge-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            Directory.CreateDirectory(Path.Combine(root, "StrykerOutput"));
            File.WriteAllText(Path.Combine(root, "bin", "skip.cs"), "// bin\n");
            File.WriteAllText(Path.Combine(root, "obj", "skip.cs"), "// obj\n");
            File.WriteAllText(Path.Combine(root, "StrykerOutput", "mutation.json"), "{}\n");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "# comment\nbin/\nobj/\nStrykerOutput/\n!keep.txt\n*.tmp\n");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "keep.cs"), "// keep\n");
            File.WriteAllText(Path.Combine(root, "note.tmp"), "tmp\n");
            // Simulate merged ignore list (manual, since TreeScanner itself doesn't read .gitignore; ScanCommand adds it).
            var mergedIgnores = new[] { "bin/", "obj/", "StrykerOutput/" };
            // Pass null for exts to use defaults; ensures .cs included.
            var files = TreeScanner.Scan(root, null, mergedIgnores, "physical");
            var paths = files.Select(f => f.Path).ToList();
            Assert.Contains("src/keep.cs", paths);
            Assert.DoesNotContain("bin/skip.cs", paths);
            Assert.DoesNotContain("obj/skip.cs", paths);
            // Non-.cs files excluded by extension filter
            Assert.DoesNotContain("StrykerOutput/mutation.json", paths);
            Assert.DoesNotContain("note.tmp", paths);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
