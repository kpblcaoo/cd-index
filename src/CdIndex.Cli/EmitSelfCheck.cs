using CdIndex.Core;
using CdIndex.Emit;
using System;
using System.IO;

namespace CdIndex.Cli;

class EmitSelfCheck
{
    public static void Run(bool scanTreeOnly = false)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var treeFiles = TreeScanner.Scan(repoRoot);
        var treeSection = new TreeSection(treeFiles.ToList());
        var index = new ProjectIndex(
            new MetaSection(
                "2025-01-01T00:00:00Z", // фиксированная дата для идемпотентности
                "0.0.1-dev",
                null
            ),
            scanTreeOnly ? new List<ProjectSection>() : new List<ProjectSection>(),
            new List<TreeSection> { treeSection },
            scanTreeOnly ? new List<DISection>() : new List<DISection>(),
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
