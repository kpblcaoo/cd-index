using CdIndex.Core;
using CdIndex.Emit;
using CdIndex.Extractors;
using CdIndex.Roslyn;
using System;
using System.IO;
using System.Linq;

namespace CdIndex.Cli;

class EmitSelfCheck
{
    public static void Run(bool scanTreeOnly = false, bool scanDi = false, bool scanEntrypoints = false)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var treeFiles = TreeScanner.Scan(repoRoot);
        var treeSection = new TreeSection(treeFiles.ToList());

        // We may need to load the solution once if either DI or Entrypoints requested.
        RoslynContext? sharedCtx = null;
        List<DiRegistration> diRegistrations = new();
        List<HostedService> diHostedServices = new();
        List<EntrypointsSection> entrypointsSections = new();

        if (!scanTreeOnly && (scanDi || scanEntrypoints))
        {
            try
            {
                var slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();
                if (slnFiles.Count > 0)
                {
                    MsBuildBootstrap.EnsureRegistered();
                    sharedCtx = SolutionLoader.LoadSolutionAsync(slnFiles[0], repoRoot).Result;
                }
            }
            catch
            {
                // swallow; sharedCtx stays null
            }
        }

        // DI extraction
        DISection diSection;
        if (scanDi && !scanTreeOnly && sharedCtx != null)
        {
            try
            {
                var diExtractor = new DiExtractor();
                diExtractor.Extract(sharedCtx);
                diRegistrations = diExtractor.Registrations.ToList();
                diHostedServices = diExtractor.HostedServices.ToList();
            }
            catch { }
        }
        diSection = new DISection(diRegistrations, diHostedServices);

        // Entrypoints extraction (with seeding if DI already scanned)
        if (scanEntrypoints && !scanTreeOnly && sharedCtx != null)
        {
            try
            {
                var entryExtractor = new EntrypointsExtractor();
                if (scanDi && diHostedServices.Count > 0)
                    entryExtractor.SeedHostedServices(diHostedServices);
                entryExtractor.Extract(sharedCtx);
                entrypointsSections = entryExtractor.Sections.ToList();
            }
            catch { }
        }

        var index = new ProjectIndex(
            new Meta(
                "0.0.1-dev",
                "1.2",
                DateTime.Parse("2025-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                null,
                null // Sections will be filled by emitter
            ),
            new List<ProjectSection>(),
            new List<TreeSection> { treeSection },
            scanTreeOnly || !scanDi ? null : new List<DISection> { diSection },
            scanTreeOnly || !scanEntrypoints ? null : entrypointsSections,
            null,
            null,
            null,
            null,
            null,
            null
        );
        using var stream = Console.OpenStandardOutput();
        JsonEmitter.Emit(index, stream);
    }
}
