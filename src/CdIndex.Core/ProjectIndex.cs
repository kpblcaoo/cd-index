// Корневая модель индекса
namespace CdIndex.Core;

public sealed record ProjectIndex(
    Meta Meta,
    IReadOnlyList<ProjectSection> Project,
    IReadOnlyList<TreeSection>? Tree,
    IReadOnlyList<DISection>? DI,
    IReadOnlyList<EntrypointsSection>? Entrypoints,
    IReadOnlyList<MessageFlowSection>? MessageFlow,
    IReadOnlyList<CallgraphsSection>? Callgraphs,
    IReadOnlyList<ConfigSection>? Configs,
    IReadOnlyList<CommandSection>? Commands,
    IReadOnlyList<CliCommandsSection>? CliCommands,
    IReadOnlyList<TestSection>? Tests
);

// Секции — сигнатуры пустые/минимальные
public sealed record Meta(
    string Version,
    string SchemaVersion,
    DateTime GeneratedAt,
    string? RepositoryUrl = null,
    IReadOnlyList<string>? Sections = null
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

public sealed record MessageFlowSection(
    IReadOnlyList<FlowNode> Nodes
);
public sealed record CallgraphsSection(
    ProjectRef Project,
    IReadOnlyList<Callgraph> Graphs
);
public sealed record Callgraph(
    string Root,
    int Depth,
    bool Truncated,
    IReadOnlyList<CallEdge> Edges
);
public sealed record CallEdge(
    string Caller,
    string Callee
);
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
public sealed record CliCommandsSection(
    ProjectRef Project,
    IReadOnlyList<CliCommand> Items
);
public sealed record CliCommand(
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Arguments,
    string File,
    int Line
);
public sealed record TestSection;

public sealed record FlowNode(
    int Order,
    string Kind,
    string Detail,
    string File,
    int Line
);

// Supporting types for P0 sections

// (Removed legacy EntrypointEntry/EntrypointSection in v1.1)
