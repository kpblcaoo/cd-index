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
public sealed record MetaSection(
    string GeneratedAt,
    string Version,
    string? RepositoryUrl = null
);

public sealed record ProjectSection(
    string Name,
    string Path,
    string? Framework = null,
    string? Language = null
);

public sealed record TreeSection(
    IReadOnlyList<Tree.FileEntry> Files
);

public sealed record DISection(
    IReadOnlyList<DIEntry> Registrations
);

public sealed record EntrypointSection(
    IReadOnlyList<EntrypointEntry> Entrypoints
);

public sealed record MessageFlowSection;
public sealed record CallgraphSection;
public sealed record ConfigSection;
public sealed record CommandSection;
public sealed record TestSection;

// Supporting types for P0 sections
public sealed record DIEntry(
    string ServiceType,
    string? ImplementationType = null,
    string Lifetime = "Transient"
);

public sealed record EntrypointEntry(
    string Name,
    string Path,
    string Type,
    string? Description = null
);
