using System.CommandLine;

namespace CdIndex.Cli;

class Program
{
    static int Main(string[] args)
    {
        const string ScanUsage = @"Usage: cd-index scan (--sln <file.sln> | --csproj <project.csproj>) [options]

Options:
    --sln <path>                     Path to solution file (mutually exclusive with --csproj)
    --csproj <path>                  Path to project file (mutually exclusive with --sln)
    --out <file>                     Write JSON to file (default stdout)
    --ext <extension>                Additional file extension to include (repeatable)
    --ignore <glob|prefix>           Path prefix or suffix to ignore (repeatable)
    --gitignore                      Merge .gitignore patterns (simple: comments/# & ! excluded)
    --sections <list>                Comma/space list of sections to enable (Tree,DI,Entrypoints,Configs,Commands,MessageFlow,Callgraphs)
    --no-tree                        Shorthand for --sections DI,Entrypoints,Configs,Commands,MessageFlow,Callgraphs
    --loc-mode <physical|logical>    LOC counting mode (default physical)
    --scan-tree / --no-scan-tree     Enable/disable tree section (default on)
    --scan-di / --no-scan-di         Enable/disable DI extraction (default on)
    --scan-entrypoints / --no-scan-entrypoints  Enable/disable entrypoints (default on)
    --scan-configs / --no-scan-configs          Enable/disable configs extraction (default off)
    --env-prefix <P>                 Add environment variable prefix filter (repeatable, default DOORMAN_)
    --scan-commands / --no-scan-commands        Enable/disable commands extraction (default off)
    --commands-router-names <names>             Comma/space separated router method names (default Map,Register,Add,On,Route,Bind)
    --commands-attr-names <names>               Comma/space separated attribute names for command discovery (default Command,Commands)
    --commands-normalize <rules>                Comma/space list: trim,ensure-slash (default trim,ensure-slash)
    --commands-dedup <mode>                     case-sensitive|case-insensitive (default case-sensitive)
    --commands-include <modes>                  router,attributes,comparison (default router,attributes)
    --commands-conflicts <mode>                 warn|error|ignore (default warn)
    --commands-conflict-report <file>           Optional JSON file with conflict details (no schema change)
    --commands-allow-regex <regex>              Override allowed command validation regex (default ^/[a-z][a-z0-9_]*$)
    --di-dedupe                                 Enable DI duplicate suppression (keep first)
    --scan-flow / --no-scan-flow     Enable/disable message flow extraction (default off)
    --flow-handler <TypeName>        Handler class name for flow (required if --scan-flow)
    --flow-method <MethodName>       Handler method name for flow (default HandleAsync)
    --flow-delegate-suffixes <list>  Comma/space list of type suffixes treated as delegates (default Router,Facade,Service,Dispatcher,Processor,Manager,Module)
    --scan-callgraphs / --no-scan-callgraphs     Enable/disable callgraph extraction (default off)
    --callgraph-method <MethodId>   Root method for callgraph (repeatable). Format: Namespace.Type.Method(/argCount optional) or Namespace.Type..ctor(/argCount)
    --max-call-depth <N>            Max traversal depth (default 2)
    --max-call-nodes <N>            Max distinct nodes visited (default 200)
    --include-external              Include external (out-of-solution) callees as leaf nodes
    --no-pretty                     Emit compact JSON (default pretty indented)
    --verbose                        Verbose diagnostics to stderr
    -h, --help                       Show this help
";

        bool IsHelp(string a) => a == "--help" || a == "-h" || a == "help";

        var versionOption = new Option<bool>("--version")
        {
            Description = "Show tool version and exit"
        };
        var selfCheckOption = new Option<bool>("--selfcheck")
        {
            Description = "Emit minimal deterministic JSON for self-check"
        };
        var scanTreeOnlyOption = new Option<bool>("--scan-tree-only")
        {
            Description = "Only scan tree section (for selfcheck)"
        };
        var scanDiOption = new Option<bool>("--scan-di")
        {
            Description = "Include DI extraction in selfcheck"
        };
        var scanEntrypointsOption = new Option<bool>("--scan-entrypoints")
        {
            Description = "Include Entrypoints extraction (Program.Main + HostedServices)"
        };
        var rootCommand = new RootCommand("cd-index CLI tool");
        rootCommand.Options.Add(versionOption);
        rootCommand.Options.Add(selfCheckOption);
        rootCommand.Options.Add(scanTreeOnlyOption);
        rootCommand.Options.Add(scanDiOption);
        rootCommand.Options.Add(scanEntrypointsOption);

        // Config commands: config init / config print
        if (args.Length > 0 && string.Equals(args[0], "config", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1 || args.Skip(1).Any(IsHelp))
            {
                Console.WriteLine("Usage: cd-index config <init|print|example> [options]\n  init     Generate cd-index.toml (use --force to overwrite)\n  print    Show merged configuration (after defaults+TOML; CLI overrides not applied here yet)\n  example  Print rich commented example (defaults only, ignores existing file).");
                return 0;
            }
            var sub = args[1];
            switch (sub)
            {
                case "init":
                    {
                        var force = args.Contains("--force");
                        var path = args.Skip(2).FirstOrDefault(a => a == "--path") != null ? args.SkipWhile(a => a != "--path").Skip(1).FirstOrDefault() : null;
                        path ??= Path.Combine(Directory.GetCurrentDirectory(), "cd-index.toml");
                        if (File.Exists(path) && !force)
                        {
                            Console.Error.WriteLine($"Config file '{path}' already exists. Use --force to overwrite.");
                            return 8;
                        }
                        var defaults = Config.ScanConfig.Defaults();
                        File.WriteAllText(path, Config.ConfigExampleBuilder.Build(defaults));
                        Console.WriteLine($"Written template config to {path}");
                        return 0;
                    }
                case "print":
                    {
                        string? explicitPath = null;
                        for (int i = 2; i < args.Length; i++)
                        {
                            if (args[i] == "--config" && i + 1 < args.Length)
                            {
                                explicitPath = args[++i];
                            }
                        }
                        var (cfg, source, diags) = ConfigLoader.Load(explicitPath, Directory.GetCurrentDirectory(), verbose: true);
                        foreach (var d in diags) Console.Error.WriteLine(d);
                        Console.WriteLine(Config.ConfigExampleBuilder.Build(cfg));
                        return 0;
                    }
                case "example":
                    {
                        var defaults = Config.ScanConfig.Defaults();
                        Console.WriteLine(Config.ConfigExampleBuilder.Build(defaults));
                        return 0;
                    }
                default:
                    Console.Error.WriteLine($"Unknown config subcommand '{sub}'");
                    return 5;
            }
        }

        // Manual 'scan' command handling (simpler & stable across System.CommandLine betas)
        if (args.Length > 0 && string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
        {
            // scan help
            if (args.Length == 1 || args.Skip(1).Any(IsHelp))
            {
                Console.WriteLine(ScanUsage);
                return 0;
            }
            var slnPath = (string?)null;
            var csprojPath = (string?)null;
            FileInfo? outFile = null;
            var exts = new List<string>();
            var ignores = new List<string>();
            bool useGitignore = false;
            List<string>? sectionsRequested = null;
            string? commandConflictReport = null;
            var locMode = "physical";
            bool scanTree = true, scanDi = true, scanEntrypoints = true, scanConfigs = false, scanCommands = false, scanFlow = false, verbose = false;
            bool scanCallgraphs = false;
            bool noPretty = false;
            string? flowHandler = null; string flowMethod = "HandleAsync"; string? flowDelegateSuffixes = null;
            var envPrefixes = new List<string>();
            var commandRouterNames = new List<string>();
            var commandAttrNames = new List<string>();
            var commandNormalize = new List<string>();
            string? commandDedup = null;
            string? commandConflicts = null;
            var commandInclude = new List<string>();
            string? commandAllowRegex = null;
            bool diDedupe = false;
            // Callgraph options
            var callgraphMethods = new List<string>();
            int? maxCallDepth = null; int? maxCallNodes = null; bool includeExternal = false;
            string? configPath = null;
            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--config": if (i + 1 < args.Length) configPath = args[++i]; else return 5; break;
                    case "--sln": if (i + 1 < args.Length) slnPath = args[++i]; else return 5; break;
                    case "--csproj": if (i + 1 < args.Length) csprojPath = args[++i]; else return 5; break;
                    case "--out": if (i + 1 < args.Length) outFile = new FileInfo(args[++i]); else return 5; break;
                    case "--ext": if (i + 1 < args.Length) exts.Add(args[++i]); else return 5; break;
                    case "--ignore": if (i + 1 < args.Length) ignores.Add(args[++i]); else return 5; break;
                    case "--gitignore": useGitignore = true; break;
                    case "--sections":
                        if (i + 1 < args.Length)
                        {
                            sectionsRequested ??= new List<string>();
                            sectionsRequested.AddRange(args[++i].Split(',', ' ', StringSplitOptions.RemoveEmptyEntries));
                        }
                        else return 5; break;
                    case "--no-tree":
                        // Previous behavior enabled MessageFlow implicitly; now treat flow as explicit opt-in only.
                        sectionsRequested = new List<string> { "DI", "Entrypoints", "Configs", "Commands", /* no MessageFlow */ "Callgraphs" };
                        break;
                    case "--loc-mode": if (i + 1 < args.Length) locMode = args[++i]; else return 5; break;
                    case "--scan-tree": scanTree = true; break; // defaults true
                    case "--scan-di": scanDi = true; break;
                    case "--scan-entrypoints": scanEntrypoints = true; break;
                    case "--no-scan-tree": scanTree = false; break;
                    case "--no-scan-di": scanDi = false; break;
                    case "--no-scan-entrypoints": scanEntrypoints = false; break;
                    case "--verbose": verbose = true; break;
                    case "--scan-configs": scanConfigs = true; break;
                    case "--no-scan-configs": scanConfigs = false; break;
                    case "--env-prefix": if (i + 1 < args.Length) envPrefixes.Add(args[++i]); else return 5; break;
                    case "--scan-commands": scanCommands = true; break;
                    case "--no-scan-commands": scanCommands = false; break;
                    case "--commands-router-names":
                        // collect following tokens until next -- or end? Simpler: single space-separated list in next arg
                        if (i + 1 < args.Length) commandRouterNames.AddRange(args[++i].Split(',', ' ', StringSplitOptions.RemoveEmptyEntries)); else return 5;
                        break;
                    case "--commands-attr-names":
                        if (i + 1 < args.Length) commandAttrNames.AddRange(args[++i].Split(',', ' ', StringSplitOptions.RemoveEmptyEntries)); else return 5;
                        break;
                    case "--commands-conflict-report":
                        if (i + 1 < args.Length) commandConflictReport = args[++i]; else return 5;
                        break;
                    case "--commands-normalize":
                        if (i + 1 < args.Length) commandNormalize.AddRange(args[++i].Split(',', ' ', StringSplitOptions.RemoveEmptyEntries)); else return 5;
                        break;
                    case "--commands-dedup":
                        if (i + 1 < args.Length) commandDedup = args[++i]; else return 5;
                        break;
                    case "--commands-conflicts":
                        if (i + 1 < args.Length) commandConflicts = args[++i]; else return 5;
                        break;
                    case "--commands-include":
                        if (i + 1 < args.Length) commandInclude.AddRange(args[++i].Split(',', ' ', StringSplitOptions.RemoveEmptyEntries)); else return 5; break;
                    case "--commands-allow-regex":
                        if (i + 1 < args.Length) commandAllowRegex = args[++i]; else return 5; break;
                    case "--di-dedupe":
                        diDedupe = true; break;
                    case "--scan-flow": scanFlow = true; break;
                    case "--no-scan-flow": scanFlow = false; break;
                    case "--flow-handler": if (i + 1 < args.Length) flowHandler = args[++i]; else return 5; break;
                    case "--flow-method": if (i + 1 < args.Length) flowMethod = args[++i]; else return 5; break;
                    case "--flow-delegate-suffixes": if (i + 1 < args.Length) flowDelegateSuffixes = args[++i]; else return 5; break;
                    case "--scan-callgraphs": scanCallgraphs = true; break;
                    case "--no-scan-callgraphs": scanCallgraphs = false; break;
                    case "--callgraph-method": if (i + 1 < args.Length) { callgraphMethods.Add(args[++i]); } else return 5; break;
                    case "--max-call-depth": if (i + 1 < args.Length && int.TryParse(args[++i], out var mcd)) maxCallDepth = mcd; else return 5; break;
                    case "--max-call-nodes": if (i + 1 < args.Length && int.TryParse(args[++i], out var mcn)) maxCallNodes = mcn; else return 5; break;
                    case "--include-external": includeExternal = true; break;
                    case "--no-pretty": noPretty = true; break;
                    case "--help":
                    case "-h":
                    case "help":
                        Console.WriteLine(ScanUsage);
                        return 0;
                    default:
                        Console.Error.WriteLine($"Unknown option for scan: {a}");
                        return 5;
                }
            }
            // Normalize & expand comma-separated lists for exts / ignores
            if (exts.Count > 0)
            {
                var norm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in exts)
                {
                    foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = token.Trim();
                        if (t.Length == 0) continue;
                        if (!t.StartsWith('.')) t = "." + t; // ensure leading dot for extension matching
                        norm.Add(t);
                    }
                }
                exts = norm.ToList();
            }
            if (ignores.Count > 0)
            {
                var norm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in ignores)
                {
                    foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = token.Trim();
                        if (t.Length == 0) continue;
                        // normalize leading slash removal
                        if (t.StartsWith("/")) t = t.TrimStart('/');
                        norm.Add(t.EndsWith("/") ? t : t); // keep as-is for prefix match
                    }
                }
                ignores = norm.ToList();
            }
            if (sectionsRequested != null && sectionsRequested.Count > 0)
            {
                scanTree = scanDi = scanEntrypoints = scanConfigs = scanCommands = scanFlow = false; // reset
                foreach (var s in sectionsRequested)
                {
                    switch (s.ToLowerInvariant())
                    {
                        case "tree": scanTree = true; break;
                        case "di": scanDi = true; break;
                        case "entrypoints": scanEntrypoints = true; break;
                        case "configs": scanConfigs = true; break;
                        case "commands": scanCommands = true; break;
                        case "messageflow": scanFlow = true; break;
                        case "callgraphs": scanCallgraphs = true; break;
                        default: Console.Error.WriteLine($"WARN: unknown section '{s}' ignored"); break;
                    }
                }
            }
            if (callgraphMethods.Count > 0) scanCallgraphs = true; // auto-enable
            // Load config (merge defaults + TOML). CLI overrides applied below manually (phase 2 TODO).
            var repoRoot = slnPath != null ? Path.GetDirectoryName(Path.GetFullPath(slnPath))! : Directory.GetCurrentDirectory();
            var (cfg, cfgSource, loadDiags) = ConfigLoader.Load(configPath, repoRoot, verbose);
            if (verbose)
            {
                foreach (var d in loadDiags) Console.Error.WriteLine(d);
            }
            // CLI overrides currently override key lists if provided (ignore/ext/sections)
            if (exts.Count > 0) cfg.Scan.Ext = exts;
            if (ignores.Count > 0) cfg.Scan.Ignore = ignores;
            if (sectionsRequested != null) cfg.Scan.Sections = sectionsRequested;
            if (cfg.Scan.NoTree) { scanTree = false; }
            if (cfg.Tree.UseGitignore) useGitignore = true; // CLI --gitignore already set earlier
            if (cfg.DI.Dedupe == "keep-first") diDedupe = true;
            if (cfg.Commands.Include.Count > 0 && commandInclude.Count == 0) commandInclude = cfg.Commands.Include;
            if (cfg.Commands.RouterNames.Count > 0 && commandRouterNames.Count == 0) commandRouterNames = cfg.Commands.RouterNames;
            if (cfg.Commands.AttrNames.Count > 0 && commandAttrNames.Count == 0) commandAttrNames = cfg.Commands.AttrNames;
            if (cfg.Commands.Normalize.Count > 0 && commandNormalize.Count == 0) commandNormalize = cfg.Commands.Normalize;
            if (commandDedup == null) commandDedup = cfg.Commands.Dedup;
            if (commandConflicts == null) commandConflicts = cfg.Commands.Conflicts;
            if (commandAllowRegex == null && !string.IsNullOrWhiteSpace(cfg.Commands.AllowRegex)) commandAllowRegex = cfg.Commands.AllowRegex;
            if (cfg.Flow.Handler != null && flowHandler == null) flowHandler = cfg.Flow.Handler;
            if (flowDelegateSuffixes == null && cfg.Flow.DelegateSuffixes.Count > 0) flowDelegateSuffixes = string.Join(',', cfg.Flow.DelegateSuffixes);

            var code = ScanCommand.Run(
                slnPath != null ? new FileInfo(slnPath) : null,
                csprojPath != null ? new FileInfo(csprojPath) : null,
                outFile,
                exts.ToArray(),
                ignores.ToArray(),
                useGitignore,
                locMode,
                scanTree,
                scanDi,
                scanEntrypoints,
                scanConfigs,
                // conflict report handled inside ScanCommand
                envPrefixes,
                scanCommands,
                scanFlow,
                flowHandler,
                flowMethod,
                verbose,
                commandRouterNames,
                commandAttrNames,
                commandNormalize,
                commandDedup,
                commandConflicts,
                commandConflictReport,
                flowDelegateSuffixes != null ? flowDelegateSuffixes.Split(',', ' ', StringSplitOptions.RemoveEmptyEntries) : null,
                commandInclude,
                diDedupe,
                commandAllowRegex,
                scanCallgraphs,
                callgraphMethods,
                maxCallDepth,
                maxCallNodes,
                includeExternal,
                noPretty);
            return code;
        }

        if (args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine("cd-index v0.0.1-dev");
            return 0;
        }
        var parseResult = rootCommand.Parse(args);
        if (parseResult.GetValue(selfCheckOption))
        {
            var scanTreeOnly = parseResult.GetValue(scanTreeOnlyOption);
            var scanDi = parseResult.GetValue(scanDiOption);
            var scanEntrypoints = parseResult.GetValue(scanEntrypointsOption);
            EmitSelfCheck.Run(scanTreeOnly, scanDi, scanEntrypoints);
            return 0;
        }
        if (args.Length == 0 || parseResult.Errors.Count > 0 || args.Any(IsHelp))
        {
            Console.WriteLine("cd-index - project index tool\n\nCommands:\n  scan    Scan solution/project and emit JSON index\n\nOptions:\n  --version         Show tool version and exit\n  --selfcheck       Emit deterministic self-check JSON\n  --scan-tree-only  Only include Tree in selfcheck\n  --scan-di         Include DI in selfcheck\n  --scan-entrypoints Include Entrypoints in selfcheck\n  -h, --help        Show help\n\nRun 'cd-index scan --help' for scan options.");
            foreach (var error in parseResult.Errors)
                Console.Error.WriteLine(error.Message);
            return 1;
        }
        // ...дополнительная логика команд...
        return 0;
    }

    // Legacy inline template removed – now uses ConfigExampleBuilder.
}
