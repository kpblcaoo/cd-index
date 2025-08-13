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
    public static void Run(bool scanTreeOnly = false, bool scanDi = false)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var treeFiles = TreeScanner.Scan(repoRoot);
        var treeSection = new TreeSection(treeFiles.ToList());

        // DI extraction if requested
        DISection diSection;
        if (scanDi && !scanTreeOnly)
        {
            try
            {
                // Look for solution files
                var slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"))
                    .ToList();
                
                if (slnFiles.Count > 0)
                {
                    MsBuildBootstrap.EnsureRegistered();
                    var ctx = SolutionLoader.LoadSolutionAsync(slnFiles[0], repoRoot).Result;
                    var diExtractor = new DiExtractor();
                    diExtractor.Extract(ctx);
                    diSection = new DISection(diExtractor.Registrations.ToList(), diExtractor.HostedServices.ToList());
                }
                else
                {
                    diSection = new DISection(new List<DiRegistration>(), new List<HostedService>());
                }
            }
            catch
            {
                // Fallback to empty DI section if extraction fails
                diSection = new DISection(new List<DiRegistration>(), new List<HostedService>());
            }
        }
        else
        {
            diSection = new DISection(new List<DiRegistration>(), new List<HostedService>());
        }

        var index = new ProjectIndex(
            new MetaSection(
                "2025-01-01T00:00:00Z", // фиксированная дата для идемпотентности
                "0.0.1-dev",
                null
            ),
            scanTreeOnly ? new List<ProjectSection>() : new List<ProjectSection>(),
            new List<TreeSection> { treeSection },
            scanTreeOnly ? new List<DISection>() : new List<DISection> { diSection },
            scanTreeOnly ? new List<EntrypointSection>() : new List<EntrypointSection>(),
            scanTreeOnly ? new List<MessageFlowSection>() : new List<MessageFlowSection>(),
            scanTreeOnly ? new List<CallgraphSection>() : new List<CallgraphSection>(),
            scanTreeOnly ? new List<ConfigSection>() : new List<ConfigSection>(),
            scanTreeOnly ? new List<CommandSection>() : new List<CommandSection>(),
            scanTreeOnly ? new List<TestSection>() : new List<TestSection>()
        );
        using var stream = Console.OpenStandardOutput();
        JsonEmitter.Emit(index, stream);
    }
}
