using System.Text;
using CdIndex.Cli.Config;

namespace CdIndex.Cli;

public static class ConfigLoader
{
    public static (ScanConfig config, string source, List<string> diagnostics) Load(string? explicitPath, string repoRoot, bool verbose)
    {
        var diags = new List<string>();
        var defaults = ScanConfig.Defaults();
        string? path = null;
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath!))
        {
            path = explicitPath;
        }
        else
        {
            var p1 = Path.Combine(repoRoot, "cd-index.toml");
            var p2 = Path.Combine(repoRoot, ".cd-index.toml");
            if (File.Exists(p1)) path = p1; else if (File.Exists(p2)) path = p2;
            else
            {
                var env = Environment.GetEnvironmentVariable("CD_INDEX_CONFIG");
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) path = env;
            }
        }
        if (path == null)
        {
            diags.Add("CFG001 No config found; using built-in defaults.");
            return (defaults, "defaults", diags);
        }
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            // Minimal TOML parse via naive key=value extraction (placeholder until Tomlyn added)
            var cfg = ParseToml(text, defaults);
            diags.Add($"CFG010 Loaded config from {Path.GetFileName(path)}.");
            return (cfg, path, diags);
        }
        catch (Exception ex)
        {
            diags.Add($"CFG900 Failed to load config '{path}': {ex.Message}; falling back to defaults.");
            return (defaults, "defaults", diags);
        }
    }

    private static ScanConfig ParseToml(string text, ScanConfig defaults)
    {
        // TEMP very small parser: only handles '[section]' and 'key = [array]' or 'key = "value"' forms; fallback replaces whole lists.
        // Replace later with Tomlyn for correctness.
        var cfg = ScanConfig.Defaults();
        string? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1].Trim(); continue; }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (current == "scan")
            {
                if (key == "ignore") cfg.Scan.Ignore = ParseArray(value);
                else if (key == "ext") cfg.Scan.Ext = ParseArray(value);
                else if (key == "noTree" && bool.TryParse(value, out var b)) cfg.Scan.NoTree = b;
                else if (key == "sections") cfg.Scan.Sections = ParseArray(value);
            }
            else if (current == "tree")
            {
                if (key == "locMode") cfg.Tree.LocMode = TrimQuotes(value);
                else if (key == "useGitignore" && bool.TryParse(value, out var b2)) cfg.Tree.UseGitignore = b2;
            }
            else if (current == "di")
            {
                if (key == "dedupe") cfg.DI.Dedupe = TrimQuotes(value);
            }
            else if (current == "commands")
            {
                if (key == "include") cfg.Commands.Include = ParseArray(value);
                else if (key == "attrNames") cfg.Commands.AttrNames = ParseArray(value);
                else if (key == "routerNames") cfg.Commands.RouterNames = ParseArray(value);
                else if (key == "normalize") cfg.Commands.Normalize = ParseArray(value);
                else if (key == "allowRegex") cfg.Commands.AllowRegex = TrimQuotes(value);
                else if (key == "dedup") cfg.Commands.Dedup = TrimQuotes(value);
                else if (key == "conflicts") cfg.Commands.Conflicts = TrimQuotes(value);
            }
            else if (current == "flow")
            {
                if (key == "handler") cfg.Flow.Handler = TrimQuotes(value);
                else if (key == "method") cfg.Flow.Method = TrimQuotes(value);
                else if (key == "delegateSuffixes") cfg.Flow.DelegateSuffixes = ParseArray(value);
            }
        }
        return cfg;
    }

    private static List<string> ParseArray(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('[') && raw.EndsWith(']')) raw = raw[1..^1];
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => TrimQuotes(p.Trim())).Where(p => p.Length > 0).ToList();
    }

    private static string TrimQuotes(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\''))))
            return v[1..^1];
        return v;
    }
}
