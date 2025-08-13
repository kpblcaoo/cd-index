using CdIndex.Core;
using CdIndex.Emit;
using System;
using System.IO;

namespace CdIndex.Cli;

class EmitSelfCheck
{
    public static void Run()
    {
        var index = new ProjectIndex(
            new MetaSection(
                "2025-01-01T00:00:00Z",
                "0.0.1-dev",
                null
            ),
            new List<ProjectSection>(),
            new List<TreeSection>(),
            new List<DISection>(),
            new List<EntrypointSection>(),
            new List<MessageFlowSection>(),
            new List<CallgraphSection>(),
            new List<ConfigSection>(),
            new List<CommandSection>(),
            new List<TestSection>()
        );
        using var stream = Console.OpenStandardOutput();
        JsonEmitter.Emit(index, stream);
    }
}
