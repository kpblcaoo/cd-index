// Корневая модель индекса
namespace CdIndex.Core;

public sealed record ProjectIndex(
    MetaSection Meta,
    IReadOnlyList<ProjectSection> Project,
    IReadOnlyList<TreeSection> Tree,
    IReadOnlyList<DISection> DI,
    IReadOnlyList<EntrypointSection> Entrypoints,
    IReadOnlyList<MessageFlowSection> MessageFlow,
    IReadOnlyList<CallgraphSection> Callgraphs,
    IReadOnlyList<ConfigSection> Configs,
    IReadOnlyList<CommandSection> Commands,
    IReadOnlyList<TestSection> Tests
);

// Секции — сигнатуры пустые/минимальные
public sealed record MetaSection;
public sealed record ProjectSection;
public sealed record TreeSection;
public sealed record DISection;
public sealed record EntrypointSection;
public sealed record MessageFlowSection;
public sealed record CallgraphSection;
public sealed record ConfigSection;
public sealed record CommandSection;
public sealed record TestSection;
