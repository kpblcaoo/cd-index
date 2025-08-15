using System.Text;
using CdIndex.Cli.Config;
using Tomlyn;
using Tomlyn.Model;

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
            var cfg = ParseTomlTomlyn(text, defaults, diags);
            diags.Add($"CFG010 Loaded config from {Path.GetFileName(path)}.");
            return (cfg, path, diags);
        }
        catch (Exception ex)
        {
            diags.Add($"CFG900 Failed to load config '{path}': {ex.Message}; falling back to defaults.");
            return (defaults, "defaults", diags);
        }
    }

    private static ScanConfig ParseTomlTomlyn(string text, ScanConfig defaults, List<string> diags)
    {
        var cfg = ScanConfig.Defaults();
        try
        {
            var doc = Toml.Parse(text);
            if (doc.HasErrors)
            {
                foreach (var e in doc.Diagnostics) diags.Add($"CFG100 {e}" );
            }
            var root = doc.ToModel();
            if (root is TomlTable table)
            {
                // scan
                if (table.TryGetValue("scan", out var scanObj) && scanObj is TomlTable scan)
                {
                    if (scan.TryGetValue("ignore", out var ig) && ig is TomlArray iga) cfg.Scan.Ignore = iga.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                    if (scan.TryGetValue("ext", out var ex) && ex is TomlArray exa) cfg.Scan.Ext = exa.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                    if (scan.TryGetValue("noTree", out var nt) && bool.TryParse(nt?.ToString(), out var bnt)) cfg.Scan.NoTree = bnt;
                    if (scan.TryGetValue("sections", out var sec) && sec is TomlArray seca) cfg.Scan.Sections = seca.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                }
                if (table.TryGetValue("tree", out var treeObj) && treeObj is TomlTable tree)
                {
                    if (tree.TryGetValue("locMode", out var lm) && lm!=null) cfg.Tree.LocMode = lm.ToString()!;
                    if (tree.TryGetValue("useGitignore", out var ug) && bool.TryParse(ug?.ToString(), out var bug)) cfg.Tree.UseGitignore = bug;
                }
                if (table.TryGetValue("di", out var diObj) && diObj is TomlTable di)
                {
                    if (di.TryGetValue("dedupe", out var dd) && dd!=null) cfg.DI.Dedupe = dd.ToString()!;
                }
                if (table.TryGetValue("commands", out var cmdObj) && cmdObj is TomlTable cmd)
                {
                    if (cmd.TryGetValue("include", out var inc) && inc is TomlArray inca) cfg.Commands.Include = inca.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                    if (cmd.TryGetValue("attrNames", out var an) && an is TomlArray ana) cfg.Commands.AttrNames = ana.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                    if (cmd.TryGetValue("routerNames", out var rn) && rn is TomlArray rna) cfg.Commands.RouterNames = rna.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                    if (cmd.TryGetValue("normalize", out var norm) && norm is TomlArray norma) cfg.Commands.Normalize = norma.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                    if (cmd.TryGetValue("allowRegex", out var ar) && ar!=null) cfg.Commands.AllowRegex = ar.ToString();
                    if (cmd.TryGetValue("dedup", out var dp) && dp!=null) cfg.Commands.Dedup = dp.ToString()!;
                    if (cmd.TryGetValue("conflicts", out var cf) && cf!=null) cfg.Commands.Conflicts = cf.ToString()!;
                }
                if (table.TryGetValue("flow", out var flowObj) && flowObj is TomlTable flow)
                {
                    if (flow.TryGetValue("handler", out var h) && h!=null) cfg.Flow.Handler = h.ToString();
                    if (flow.TryGetValue("method", out var m) && m!=null) cfg.Flow.Method = m.ToString()!;
                    if (flow.TryGetValue("delegateSuffixes", out var ds) && ds is TomlArray dsa) cfg.Flow.DelegateSuffixes = dsa.OfType<object?>().Select(v=>v?.ToString()??"").Where(s=>s.Length>0).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            diags.Add($"CFG901 TOML parse error: {ex.Message}");
        }
        return cfg;
    }

    // Legacy helpers removed (Tomlyn handles parsing)
}
