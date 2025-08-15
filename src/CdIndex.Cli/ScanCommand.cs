using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using CdIndex.Core;
using CdIndex.Roslyn;
using CdIndex.Extractors;
using CdIndex.Emit;
using Microsoft.CodeAnalysis;

namespace CdIndex.Cli;

internal static class ScanCommand
{
    public static int Run(FileInfo? sln, FileInfo? csproj, FileInfo? outFile, string[] exts, string[] ignores, bool useGitignore, string locMode,
        bool scanTree, bool scanDi, bool scanEntrypoints, bool scanConfigs, List<string> envPrefixes, bool scanCommands,
        bool scanFlow, string? flowHandler, string flowMethod, bool verbose, List<string>? commandRouterNames = null,
        List<string>? commandAttrNames = null, List<string>? commandNormalize = null, string? commandDedup = null,
        string? commandConflicts = null, string? commandConflictReport = null, IEnumerable<string>? flowDelegateSuffixes = null,
        List<string>? commandsInclude = null, bool diDedupe = false, string? commandAllowRegex = null,
    bool scanCallgraphs = false, List<string>? callgraphMethods = null, int? maxCallDepth = null, int? maxCallNodes = null, bool includeExternal = false,
    bool noPretty = false,
    bool scanCliCommands = false,
    string? cliAllowRegex = null)
    {
        var hasSln = sln != null;
        var hasProj = csproj != null;
        if (hasSln == hasProj) // either both or neither
        {
            Console.Error.WriteLine("ERROR: specify exactly one of --sln or --csproj");
            return 5;
        }
        string targetPath = hasSln ? sln!.FullName : csproj!.FullName;
        if (!File.Exists(targetPath))
        {
            Console.Error.WriteLine("ERROR: file not found: " + targetPath);
            return 5;
        }
        var repoRoot = Path.GetDirectoryName(targetPath)!; // simplification; could walk up
        if (verbose) Console.Error.WriteLine($"[scan] repository root: {repoRoot}");

        RoslynContext? roslyn = null;
        try
        {
            if (verbose) Console.Error.WriteLine("[scan] loading Roslyn context...");
            roslyn = hasSln
                ? SolutionLoader.LoadSolutionAsync(targetPath, repoRoot).GetAwaiter().GetResult()
                : SolutionLoader.LoadProjectAsync(targetPath, repoRoot).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: failed to load solution/project: " + ex.Message);
            return 3;
        }

        if (verbose) Console.Error.WriteLine("[scan] building sections...");

        var meta = new Meta("1.0", "1.2", DateTime.UtcNow, null, null);

        // Project sections: minimal for now (one per project in solution)
        var repoRootNorm = NormalizePath(repoRoot);
        var projectSections = roslyn.Solution.Projects
            .Select(p =>
            {
                var full = NormalizePath(p.FilePath ?? string.Empty);
                var rel = full.StartsWith(repoRootNorm, StringComparison.OrdinalIgnoreCase)
                    ? full.Substring(repoRootNorm.Length).TrimStart('/')
                    : full;
                return new ProjectSection(p.Name, rel, null, p.Language);
            })
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var treeSections = new List<TreeSection>();
        if (scanTree)
        {
            var ignoreList = ignores.ToList();
            if (useGitignore)
            {
                try
                {
                    var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
                    if (File.Exists(gitIgnorePath))
                    {
                        foreach (var line in File.ReadAllLines(gitIgnorePath))
                        {
                            var trimmed = line.Trim();
                            if (trimmed.Length == 0) continue; // skip empty
                            if (trimmed.StartsWith("#")) continue; // comment
                            if (trimmed.StartsWith("!")) continue; // skip negation for P1
                            // simple patterns: treat directory entries ending with / as prefix, others as prefix as well
                            // remove leading ./ if present
                            if (trimmed.StartsWith("./")) trimmed = trimmed.Substring(2);
                            // normalize backslashes
                            trimmed = trimmed.Replace('\\', '/');
                            if (!trimmed.EndsWith('/'))
                            {
                                // If pattern contains glob chars *, treat up to first * as prefix (simplification)
                                var star = trimmed.IndexOf('*');
                                if (star >= 0)
                                {
                                    trimmed = trimmed.Substring(0, star);
                                }
                            }
                            if (trimmed.Length == 0) continue;
                            if (!ignoreList.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                                ignoreList.Add(trimmed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARN: failed to parse .gitignore: " + ex.Message);
                }
            }
            var treeFiles = TreeScanner.Scan(repoRoot, exts.Length > 0 ? exts : null, ignoreList.Count > 0 ? ignoreList : null, locMode);
            treeSections.Add(new TreeSection(treeFiles));
        }

        DISection diSection = new(new List<DiRegistration>(), new List<HostedService>());
        if (scanDi)
        {
            try
            {
                var diExtractor = new DiExtractor(diDedupe: diDedupe);
                diExtractor.Extract(roslyn);
                diSection = new DISection(diExtractor.Registrations.ToList(), diExtractor.HostedServices.ToList());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: DI extraction failed: " + ex.Message);
            }
        }

        var entrySections = new List<EntrypointsSection>();
        if (scanEntrypoints)
        {
            try
            {
                var entryExtractor = new EntrypointsExtractor();
                if (scanDi && diSection.HostedServices.Count > 0)
                    entryExtractor.SeedHostedServices(diSection.HostedServices);
                entryExtractor.Extract(roslyn);
                entrySections.AddRange(entryExtractor.Sections);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: Entrypoints extraction failed: " + ex.Message);
            }
        }

        // Determinism ordering pass via emitter (emitter re-sorts), but ensure empty lists created when disabled
        var configSections = new List<ConfigSection>();
        if (scanConfigs)
        {
            try
            {
                var cfgExtractor = new ConfigExtractor(envPrefixes.Count > 0 ? envPrefixes : null);
                cfgExtractor.Extract(roslyn);
                configSections.Add(cfgExtractor.CreateSection());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: Configs extraction failed: " + ex.Message);
            }
        }

        var commandSections = new List<CommandSection>();
        if (scanCommands)
        {
            try
            {
                if (verbose)
                {
                    var incList = commandsInclude == null || commandsInclude.Count == 0
                        ? new[] { "router", "attributes" }
                        : commandsInclude.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
                    Console.Error.WriteLine($"CMD001 commands sources enabled: {string.Join(',', incList)}");
                    if (!string.IsNullOrWhiteSpace(commandDedup))
                        Console.Error.WriteLine($"CMD002 dedupe mode: {commandDedup}");
                    if (!string.IsNullOrWhiteSpace(commandAllowRegex))
                        Console.Error.WriteLine($"CMD003 allowRegex override: {commandAllowRegex}");
                }
                // TODO: wire actual flags for case-insensitive, allowBare, normalization selection
                var commandDedupMode = commandDedup; // null -> case-sensitive
                var normTrim = commandNormalize == null || commandNormalize.Count == 0 || commandNormalize.Contains("trim", StringComparer.OrdinalIgnoreCase);
                var normSlash = commandNormalize == null || commandNormalize.Count == 0 || commandNormalize.Contains("ensure-slash", StringComparer.OrdinalIgnoreCase);
                bool includeComparison = commandsInclude != null && commandsInclude.Any(i => string.Equals(i, "comparison", StringComparison.OrdinalIgnoreCase));
                bool includeRouter = commandsInclude == null || commandsInclude.Count == 0 || commandsInclude.Any(i => string.Equals(i, "router", StringComparison.OrdinalIgnoreCase));
                bool includeAttributes = commandsInclude == null || commandsInclude.Count == 0 || commandsInclude.Any(i => string.Equals(i, "attributes", StringComparison.OrdinalIgnoreCase));
                var allowRegex = commandAllowRegex ?? "^/[a-z][a-z0-9_]*$"; // default conservative pattern
                var cmdExtractor = new CommandsExtractor(
                    commandRouterNames != null && commandRouterNames.Count > 0 ? commandRouterNames : null,
                    commandAttrNames != null && commandAttrNames.Count > 0 ? commandAttrNames : null,
                    caseInsensitive: commandDedupMode == "case-insensitive" || commandDedupMode == "ci",
                    allowBare: false,
                    normalizeTrim: normTrim,
                    normalizeEnsureSlash: normSlash,
                    includeRouter: includeRouter,
                    includeAttributes: includeAttributes,
                    includeComparisons: includeComparison,
                    allowRegex: allowRegex);
                cmdExtractor.Extract(roslyn);
                var conflictsMode = (commandConflicts ?? "warn").ToLowerInvariant();
                if (cmdExtractor.Conflicts.Count > 0)
                {
                    var orderedConflicts = cmdExtractor.Conflicts
                        .GroupBy(c => c.CanonicalCommand)
                        .Select(g => new { Key = g.Key, Variants = g.SelectMany(v => v.Variants).Distinct().ToList() })
                        .OrderBy(x => x.Key, StringComparer.Ordinal)
                        .ToList();
                    if (conflictsMode is "warn" or "error")
                    {
                        foreach (var c in orderedConflicts)
                        {
                            foreach (var v in c.Variants.OrderBy(v => v.Command, StringComparer.Ordinal))
                            {
                                Console.Error.WriteLine($"CMD300 COMMAND-CONFLICT {c.Key} -> {v.Command} (handler={v.Handler ?? "<null>"} {v.File}:{v.Line})");
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(commandConflictReport))
                    {
                        try
                        {
                            using var sw = new StreamWriter(commandConflictReport!, false);
                            sw.Write("[");
                            bool firstC = true;
                            foreach (var c in orderedConflicts)
                            {
                                if (!firstC) sw.Write(','); firstC = false;
                                sw.Write("{\"canonical\":\""); sw.Write(c.Key); sw.Write("\",\"variants\":[");
                                bool firstV = true;
                                foreach (var v in c.Variants.OrderBy(v => v.Command, StringComparer.Ordinal))
                                {
                                    if (!firstV) sw.Write(','); firstV = false;
                                    sw.Write("{\"command\":\""); sw.Write(v.Command);
                                    sw.Write("\",\"handler\":"); sw.Write(v.Handler != null ? $"\"{v.Handler}\"" : "null");
                                    // file/line
                                    sw.Write(",\"file\":\"");
                                    sw.Write(v.File);
                                    sw.Write("\",\"line\":");
                                    sw.Write(v.Line);
                                    sw.Write("}");
                                }
                                sw.Write("]}");
                            }
                            sw.Write("]");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("WARN: failed writing conflict report: " + ex.Message);
                        }
                    }
                    if (conflictsMode == "error")
                    {
                        return 12;
                    }
                }
                commandSections.Add(new CommandSection(cmdExtractor.Items.ToList()));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: Commands extraction failed: " + ex.Message);
            }
        }

        var flowSections = new List<MessageFlowSection>();
        if (scanFlow)
        {
            if (string.IsNullOrWhiteSpace(flowHandler))
            {
                Console.Error.WriteLine("ERROR: --flow-handler required when --scan-flow is specified");
                return 5;
            }
            if (verbose)
            {
                var suffixesList = (flowDelegateSuffixes ?? Array.Empty<string>()).Any()
                    ? string.Join(',', (flowDelegateSuffixes ?? Array.Empty<string>()).OrderBy(s => s, StringComparer.Ordinal))
                    : "Router,Facade,Service,Dispatcher,Processor,Manager,Module";
                Console.Error.WriteLine($"FLW010 flow handler={flowHandler} method={flowMethod} delegateSuffixes={suffixesList}");
            }
            try
            {
                var flowExtractor = new FlowExtractor(flowHandler!, flowMethod, verbose, msg => Console.Error.WriteLine(msg), flowDelegateSuffixes);
                flowExtractor.Extract(roslyn);
                flowSections.Add(new MessageFlowSection(flowExtractor.Nodes.ToList()));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine((ex is InvalidOperationException ? "ERROR" : "WARN") + ": Flow extraction failed: " + ex.Message);
                if (ex is InvalidOperationException) return 5;
            }
        }
        else if (verbose)
        {
            Console.Error.WriteLine("FLW001 flow disabled (enable with --scan-flow)");
        }

        // Callgraphs (optional)
        var callgraphSections = new List<CallgraphsSection>();
        if (scanCallgraphs)
        {
            try
            {
                var cgExtractor = new CallgraphExtractor(
                    callgraphMethods ?? new List<string>(),
                    maxDepth: maxCallDepth ?? 2,
                    maxNodes: maxCallNodes ?? 200,
                    includeExternal: includeExternal,
                    verbose: verbose,
                    log: msg => { if (verbose) Console.Error.WriteLine(msg); });
                cgExtractor.Extract(roslyn);
                callgraphSections.AddRange(cgExtractor.Sections);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: Callgraph extraction failed: " + ex.Message);
            }
        }

        // CLI commands extraction placeholder (implemented in p1-C1): keep empty for now unless future flag used.
        CliCommandsSection[]? cliCommandsSections = null; // populated when --scan-cli-commands implemented
        if (scanCliCommands)
        {
            try
            {
                var extractor = new CliCommandsExtractor(cliAllowRegex);
                extractor.Extract(roslyn);
                // Group by project using file path prefix resolution
                var byProject = roslyn.Solution.Projects.ToDictionary(p => p, p => new List<CliCommand>());
                foreach (var item in extractor.Items)
                {
                    // naive: locate project whose directory is prefix of file path
                    Microsoft.CodeAnalysis.Project? owner = null;
                    foreach (var p in roslyn.Solution.Projects)
                    {
                        var projDir = NormalizePath(Path.GetDirectoryName(p.FilePath!) ?? string.Empty) + "/";
                        if (item.File.StartsWith(projDir, StringComparison.OrdinalIgnoreCase)) { owner = p; break; }
                    }
                    if (owner == null) owner = roslyn.Solution.Projects.First();
                    byProject[owner].Add(item);
                }
                var sections = new List<CliCommandsSection>();
                foreach (var kv in byProject)
                {
                    if (kv.Value.Count == 0) continue;
                    var projectPathFull = NormalizePath(kv.Key.FilePath ?? string.Empty);
                    var projRel = projectPathFull.StartsWith(repoRootNorm, StringComparison.OrdinalIgnoreCase) ? projectPathFull.Substring(repoRootNorm.Length).TrimStart('/') : projectPathFull;
                    sections.Add(new CliCommandsSection(new ProjectRef(kv.Key.Name, projRel), kv.Value
                        .OrderBy(i => i.Name, StringComparer.Ordinal)
                        .ThenBy(i => i.File, StringComparer.Ordinal)
                        .ThenBy(i => i.Line)
                        .ToList()));
                }
                if (sections.Count > 0)
                {
                    cliCommandsSections = sections.OrderBy(c => c.Project.Name, StringComparer.Ordinal).ThenBy(c => c.Project.File, StringComparer.Ordinal).ToArray();
                    if (verbose)
                    {
                        var total = cliCommandsSections.Sum(s => s.Items.Count);
                        Console.Error.WriteLine($"CLI001 collected {total} cli commands");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: CLI commands extraction failed: " + ex.Message);
            }
        }

        var index = new ProjectIndex(
            meta,
            projectSections,
            treeSections.Count == 0 ? null : treeSections,
            scanDi && diSection.Registrations.Count + diSection.HostedServices.Count > 0 ? new[] { diSection } : null,
            entrySections.Count == 0 ? null : entrySections,
            flowSections.Count == 0 ? null : flowSections,
            callgraphSections.Count == 0 ? null : callgraphSections,
            configSections.Count == 0 ? null : configSections,
            commandSections.Count == 0 ? null : commandSections,
            cliCommandsSections != null && cliCommandsSections.Length > 0 ? cliCommandsSections : null,
            null
        );

        try
        {
            if (outFile == null)
            {
                if (verbose) Console.Error.WriteLine("[scan] writing JSON to STDOUT");
                JsonEmitter.Emit(index, Console.OpenStandardOutput(), pretty: !noPretty);
            }
            else
            {
                if (verbose) Console.Error.WriteLine($"[scan] writing JSON file: {outFile.FullName}");
                using var fs = File.Create(outFile.FullName);
                JsonEmitter.Emit(index, fs, pretty: !noPretty);
            }
            if (verbose) Console.Error.WriteLine("[scan] done");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            // Map msbuild init issues if they leaked here
            if (ex.Message.Contains("MSBuild SDK not found", StringComparison.OrdinalIgnoreCase))
                return 2;
            Console.Error.WriteLine("ERROR: serialization failed: " + ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: serialization failed: " + ex.Message);
            return 4;
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
