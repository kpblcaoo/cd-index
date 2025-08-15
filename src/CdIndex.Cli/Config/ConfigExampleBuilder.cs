using System.Text;
using System.Linq;

namespace CdIndex.Cli.Config;

/// <summary>
/// Builds a rich commented example configuration (TOML) for cd-index.
/// Deterministic output: no timestamps, values ordered, only data from passed defaults.
/// </summary>
public static class ConfigExampleBuilder
{
    public static string Build(ScanConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# cd-index configuration (TOML)");
        sb.AppendLine("# Generated example. Lines starting with '#' are comments or disabled options.");
        sb.AppendLine("# Remove '#' to activate optional blocks.");
        sb.AppendLine();
        sb.AppendLine("# --- Global scan options ---");
        sb.AppendLine("[scan]");
        sb.AppendLine("# ignore: directories / path prefixes to exclude (repo-relative).");
        sb.AppendLine("ignore = [" + string.Join(',', cfg.Scan.Ignore.Select(Quote)) + "]");
        sb.AppendLine("# ext: additional file extensions (always include solution & project metadata separately).");
        sb.AppendLine("ext = [" + string.Join(',', cfg.Scan.Ext.Select(Quote)) + "]");
        sb.AppendLine("# noTree: if true, skip Tree section entirely.");
        sb.AppendLine("noTree = " + Bool(cfg.Scan.NoTree));
        sb.AppendLine("# sections: explicit subset override. Valid: Tree,DI,Entrypoints,Configs,Commands,MessageFlow,Callgraphs");
        sb.AppendLine("# NOTE: Neutral defaults: Commands & MessageFlow are NOT enabled unless you add them here or pass --scan-commands / --scan-flow.");
        sb.AppendLine("sections = [" + string.Join(',', cfg.Scan.Sections.Select(Quote)) + "]");
        sb.AppendLine();
        sb.AppendLine("# --- File tree settings ---");
        sb.AppendLine("[tree]");
        sb.AppendLine("# locMode: physical (raw line endings) | logical (normalize CRLF) – currently physical & logical treated similarly except CRLF counting.");
        sb.AppendLine("locMode = \"" + cfg.Tree.LocMode + "\"");
        sb.AppendLine("# useGitignore: if true merges simple patterns from .gitignore (no negation support yet).");
        sb.AppendLine("useGitignore = " + Bool(cfg.Tree.UseGitignore));
        sb.AppendLine();
        sb.AppendLine("# --- Dependency Injection extraction ---");
        sb.AppendLine("[di]");
        sb.AppendLine("# dedupe: keep-all | keep-first (when the same service interface is registered multiple times)");
        sb.AppendLine("dedupe = \"" + cfg.DI.Dedupe + "\"");
        sb.AppendLine("# Example alternative:\n# dedupe = \"keep-first\"");
        sb.AppendLine();
        sb.AppendLine("# --- Commands extraction (opt-in) ---");
        sb.AppendLine("[commands]");
        sb.AppendLine("# include: discovery modes (router,attributes,comparison). 'comparison' reserved for future diff tooling.");
        sb.AppendLine("include = [" + string.Join(',', cfg.Commands.Include.Select(Quote)) + "]");
        sb.AppendLine("# attrNames: attribute names (without trailing Attribute) to treat as command containers.");
        sb.AppendLine("attrNames = [" + string.Join(',', cfg.Commands.AttrNames.Select(Quote)) + "]");
        sb.AppendLine("# routerNames: method names on builder/route types used to register commands.");
        sb.AppendLine("routerNames = [" + string.Join(',', cfg.Commands.RouterNames.Select(Quote)) + "]");
        sb.AppendLine("# normalize: transforms for route values. Supported: trim,ensure-slash");
        sb.AppendLine("normalize = [" + string.Join(',', cfg.Commands.Normalize.Select(Quote)) + "]");
        sb.AppendLine("# allowRegex: validation regex; null disables validation.");
        sb.AppendLine("allowRegex = \"" + (cfg.Commands.AllowRegex ?? "") + "\"");
        sb.AppendLine("# dedup: case-sensitive | case-insensitive");
        sb.AppendLine("dedup = \"" + cfg.Commands.Dedup + "\"");
        sb.AppendLine("# conflicts: warn | error | ignore – how to handle duplicates after normalization.");
        sb.AppendLine("conflicts = \"" + cfg.Commands.Conflicts + "\"");
        sb.AppendLine();
        sb.AppendLine("# --- Message flow extraction (opt-in) ---");
        sb.AppendLine("[flow]");
        sb.AppendLine("# handler: fully-qualified type name of the root handler (required when enabling MessageFlow).");
        sb.AppendLine("handler = " + (cfg.Flow.Handler != null ? Quote(cfg.Flow.Handler) : "null"));
        sb.AppendLine("# method: handler method name to start traversal.");
        sb.AppendLine("method = \"" + cfg.Flow.Method + "\"");
        sb.AppendLine("# delegateSuffixes: type name suffixes treated as delegation hubs.");
        sb.AppendLine("delegateSuffixes = [" + string.Join(',', cfg.Flow.DelegateSuffixes.Select(Quote)) + "]");
        sb.AppendLine("# Example customization:\n# delegateSuffixes = [\"Router\",\"Dispatcher\"]");
        sb.AppendLine();
        sb.AppendLine("# --- Callgraph extraction (commented – enable via CLI today) ---");
        sb.AppendLine("# Planned TOML section for future use. Currently configure via CLI flags: ");
        sb.AppendLine("#   --scan-callgraphs --callgraph-method Namespace.Type.Method(/argCount) --max-call-depth N --max-call-nodes N --include-external");
        sb.AppendLine("# Example prospective section (NOT parsed yet):");
        sb.AppendLine("# [callgraph]");
        sb.AppendLine("# methods = [\"MyApp.Services.UserService.GetUser/1\", \"MyApp.Api.Program.Main/0\"]");
        sb.AppendLine("# maxDepth = 2");
        sb.AppendLine("# maxNodes = 200");
        sb.AppendLine("# includeExternal = false");
        sb.AppendLine();
        sb.AppendLine("# End of config.");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s + "\"";
    private static string Bool(bool b) => b ? "true" : "false";
}
