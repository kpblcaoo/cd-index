// Корневая модель индекса
namespace CdIndex.Core;

public sealed record ProjectIndex(
    Meta Meta,
    IReadOnlyList<ProjectSection> Project,
    IReadOnlyList<TreeSection> Tree,
    IReadOnlyList<DISection> DI,
    IReadOnlyList<EntrypointsSection> Entrypoints,
    IReadOnlyList<MessageFlowSection> MessageFlow,
    IReadOnlyList<CallgraphSection> Callgraphs,
    IReadOnlyList<ConfigSection> Configs,
    IReadOnlyList<CommandSection> Commands,
    IReadOnlyList<TestSection> Tests
);

// Секции — сигнатуры пустые/минимальные
public sealed record Meta(
    string Version,
    string SchemaVersion,
    DateTime GeneratedAt,
    string? RepositoryUrl = null
);

public sealed record ProjectSection(
    string Name,
    string Path,
    string? Framework = null,
    string? Language = null
);

public sealed record TreeSection(
    IReadOnlyList<FileEntry> Files
);

public sealed record DISection(
    IReadOnlyList<DiRegistration> Registrations,
    IReadOnlyList<HostedService> HostedServices
);

public sealed record EntrypointsSection(
    ProjectRef Project,
    ProgramMain? ProgramMain,
    IReadOnlyList<HostedService> HostedServices
);

public sealed record ProjectRef(string Name, string File);
public sealed record ProgramMain(string File, int Line, string? TypeName);

public sealed record MessageFlowSection;
public sealed record CallgraphSection;
public sealed record ConfigSection(
    IReadOnlyList<string> EnvKeys,
    IReadOnlyList<string> AppProps
);
public sealed record CommandSection(
    IReadOnlyList<CommandItem> Items
);
public sealed record CommandItem(
    string Command,
    string? Handler,
    string File,
    int Line
);
public sealed record TestSection;

// Supporting types for P0 sections

// (Removed legacy EntrypointEntry/EntrypointSection in v1.1)
