using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CdIndex.Core;

namespace CdIndex.Core;

public static class TreeScanner
{
    private static readonly string[] DefaultExts = new[] { ".cs", ".csproj", ".sln", ".feature", ".json", ".yaml", ".yml" };
    private static readonly string[] DefaultIgnores = new[] { "bin/", "obj/", ".git/", "logs/", ".Designer.cs" };

    public static IReadOnlyList<FileEntry> Scan(string repoRoot, IEnumerable<string>? includeExts = null, IEnumerable<string>? ignoreGlobs = null, string locMode = "physical")
    {
        includeExts ??= DefaultExts;
        ignoreGlobs ??= DefaultIgnores;
        var files = Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories)
            .Where(f => ShouldInclude(f, repoRoot, includeExts, ignoreGlobs));
        var entries = files.Select(f => ScanFile(f, repoRoot, locMode)).ToList();
        entries.Sort(ByPathComparer.Instance);
        return entries;
    }

    private static bool ShouldInclude(string filePath, string repoRoot, IEnumerable<string> exts, IEnumerable<string> ignores)
    {
        var relPath = NormalizePath(Path.GetRelativePath(repoRoot, filePath));
        var fileName = Path.GetFileName(relPath).ToLowerInvariant();
        if (fileName == "out.json" || fileName == "out-win.json") return false;
        foreach (var ig in ignores)
        {
            if (ig.StartsWith(".")) // .Designer.cs
            {
                if (relPath.EndsWith(ig, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (relPath.StartsWith(ig, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return exts.Any(ext => relPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static FileEntry ScanFile(string filePath, string repoRoot, string locMode)
    {
        var relPath = NormalizePath(Path.GetRelativePath(repoRoot, filePath));
        var kind = GetKind(relPath);
        var (sha, loc) = GetShaAndLoc(filePath, locMode);
        return new FileEntry(relPath, kind, loc, sha);
    }

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    private static string GetKind(string relPath)
    {
        var ext = Path.GetExtension(relPath);
        return ext.Length > 1 ? ext.Substring(1).ToLowerInvariant() : string.Empty;
    }

    private static (string sha, int loc) GetShaAndLoc(string filePath, string locMode)
    {
        const int maxTries = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                using var sha256 = SHA256.Create();
                int loc = 0;
                var buffer = new StringBuilder();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    buffer.Append(line);
                    buffer.Append('\n');
                    if (string.Equals(locMode, "logical", StringComparison.OrdinalIgnoreCase))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length == 0) continue; // skip empty
                        if (trimmed.StartsWith("//")) continue; // skip single-line comment
                        // naive detection of block comment lines
                        if (trimmed.StartsWith("/*") || trimmed.StartsWith("* ") || trimmed.StartsWith("*/")) continue;
                    }
                    loc++;
                }
                // Remove BOM if present
                var text = buffer.ToString();
                if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
                return (BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant(), loc);
            }
            catch (IOException) when (attempt < maxTries)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }

    private sealed class ByPathComparer : IComparer<FileEntry>
    {
        public static readonly ByPathComparer Instance = new();
        public int Compare(FileEntry? x, FileEntry? y)
            => StringComparer.OrdinalIgnoreCase.Compare(x?.Path, y?.Path);
    }
}
