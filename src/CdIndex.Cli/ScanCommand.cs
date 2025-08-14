using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using CdIndex.Core;
using CdIndex.Roslyn;
using CdIndex.Extractors;
using CdIndex.Emit;

namespace CdIndex.Cli;

internal static class ScanCommand
{
    public static int Run(FileInfo? sln, FileInfo? csproj, FileInfo? outFile, string[] exts, string[] ignores, string locMode, bool scanTree, bool scanDi, bool scanEntrypoints, bool verbose)
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

        var meta = new Meta("1.0", "1.1", DateTime.UtcNow, null);

        // Project sections: minimal for now (one per project in solution)
        var projectSections = roslyn.Solution.Projects
            .Select(p => new ProjectSection(p.Name, NormalizePath(p.FilePath ?? string.Empty), null, p.Language))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var treeSections = new List<TreeSection>();
        if (scanTree)
        {
            var treeFiles = TreeScanner.Scan(repoRoot, exts.Length > 0 ? exts : null, ignores.Length > 0 ? ignores : null);
            treeSections.Add(new TreeSection(treeFiles));
        }

        DISection diSection = new(new List<DiRegistration>(), new List<HostedService>());
        if (scanDi)
        {
            try
            {
                var diExtractor = new DiExtractor();
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
        var index = new ProjectIndex(
            meta,
            projectSections,
            treeSections,
            scanDi ? new[] { diSection } : Array.Empty<DISection>(),
            entrySections,
            Array.Empty<MessageFlowSection>(),
            Array.Empty<CallgraphSection>(),
            Array.Empty<ConfigSection>(),
            Array.Empty<CommandSection>(),
            Array.Empty<TestSection>()
        );

        try
        {
            if (outFile == null)
            {
                if (verbose) Console.Error.WriteLine("[scan] writing JSON to STDOUT");
                JsonEmitter.Emit(index, Console.OpenStandardOutput());
            }
            else
            {
                if (verbose) Console.Error.WriteLine($"[scan] writing JSON file: {outFile.FullName}");
                using var fs = File.Create(outFile.FullName);
                JsonEmitter.Emit(index, fs);
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
