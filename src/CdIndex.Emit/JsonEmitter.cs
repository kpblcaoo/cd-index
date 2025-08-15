using System.Text.Json;
using System.Text.Json.Serialization;
using CdIndex.Core;

namespace CdIndex.Emit;

public static class JsonEmitter
{
    private static readonly JsonSerializerOptions s_compact = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions s_pretty = CreateOptions(writeIndented: true);

    public static void Emit(ProjectIndex projectIndex, Stream stream, bool pretty = true)
    {
        using var _ = InvariantCultureScope.Enter();

        // Order all collections for deterministic output
        var orderedIndex = OrderCollections(projectIndex);

        JsonSerializer.Serialize(stream, orderedIndex, pretty ? s_pretty : s_compact);
    }

    public static string EmitString(ProjectIndex projectIndex, bool pretty = true)
    {
        using var stream = new MemoryStream();
        Emit(projectIndex, stream, pretty);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ProjectIndex OrderCollections(ProjectIndex index)
    {
        // Order project sections (by Name)
        var orderedProjects = Orderer.Sort(
            index.Project,
            Comparer<ProjectSection>.Create((x, y) =>
                string.Compare(x.Name, y.Name, StringComparison.Ordinal)));

        // Order tree sections and their files
        var orderedTree = new List<TreeSection>();
        if (index.Tree != null)
        {
            orderedTree = Orderer.Sort(
                index.Tree.Select(t => new TreeSection(
                    Orderer.Sort(t.Files,
                        Comparer<FileEntry>.Create((x, y) =>
                            string.Compare(x.Path, y.Path, StringComparison.InvariantCulture))))),
                Comparer<TreeSection>.Create((_, _) => 0)).ToList(); // TreeSections don't have natural order yet
        }

        // Order DI sections and registrations
        var orderedDI = new List<DISection>();
        if (index.DI != null)
        {
            var orderedDISections = index.DI
                .Select(di => new DISection(
                    Orderer.Sort(di.Registrations,
                        Comparer<DiRegistration>.Create((x, y) =>
                            string.Compare(x.Interface, y.Interface, StringComparison.Ordinal))),
                    Orderer.Sort(di.HostedServices,
                        Comparer<HostedService>.Create((x, y) =>
                        {
                            var t = string.Compare(x.Type, y.Type, StringComparison.Ordinal);
                            if (t != 0) return t;
                            var f = string.Compare(x.File, y.File, StringComparison.Ordinal);
                            if (f != 0) return f;
                            return x.Line.CompareTo(y.Line);
                        }))
                ))
                .ToList();
            orderedDI = Orderer.Sort(
                orderedDISections,
                Comparer<DISection>.Create((_, _) => 0)).ToList();
        }

        // Order entrypoints sections (by Project.Name then Project.File). HostedServices inside each already sorted below.
        var orderedEntrypoints = new List<EntrypointsSection>();
        if (index.Entrypoints != null)
        {
            orderedEntrypoints = Orderer.Sort(
                index.Entrypoints
                    .Select(ep => new EntrypointsSection(
                        ep.Project,
                        ep.ProgramMain,
                        Orderer.Sort(ep.HostedServices,
                            Comparer<HostedService>.Create((x, y) =>
                            {
                                var t = string.Compare(x.Type, y.Type, StringComparison.Ordinal);
                                if (t != 0) return t;
                                var f = string.Compare(x.File, y.File, StringComparison.Ordinal);
                                if (f != 0) return f;
                                return x.Line.CompareTo(y.Line);
                            }))
                    ))
                    .ToList(),
                Comparer<EntrypointsSection>.Create((a, b) =>
                {
                    var n = string.Compare(a.Project.Name, b.Project.Name, StringComparison.Ordinal);
                    if (n != 0) return n;
                    return string.Compare(a.Project.File, b.Project.File, StringComparison.Ordinal);
                })).ToList();
        }

        // Order Config sections (each contains sorted lists already, but ensure deterministic ordering if multiple later)
        var orderedConfigs = new List<ConfigSection>();
        if (index.Configs != null)
        {
            orderedConfigs = Orderer.Sort(
                index.Configs.Select(c => new ConfigSection(
                    c.EnvKeys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    c.AppProps.OrderBy(x => x, StringComparer.Ordinal).ToList()
                )),
                Comparer<ConfigSection>.Create((_, _) => 0)).ToList();
        }
        // Order callgraphs if present
        IReadOnlyList<CallgraphsSection>? orderedCallgraphs = null;
        if (index.Callgraphs != null)
        {
            orderedCallgraphs = Orderer.Sort(index.Callgraphs.Select(sec => new CallgraphsSection(
                        sec.Project,
                        Orderer.Sort(sec.Graphs.Select(g => new Callgraph(
                            g.Root,
                            g.Depth,
                            g.Truncated,
                            Orderer.Sort(
                                g.Edges,
                                Comparer<CallEdge>.Create((a, b) =>
                                {
                                    var c = string.Compare(a.Caller, b.Caller, StringComparison.Ordinal);
                                    if (c != 0) return c;
                                    return string.Compare(a.Callee, b.Callee, StringComparison.Ordinal);
                                }))
                        )),
                            Comparer<Callgraph>.Create((a, b) => string.Compare(a.Root, b.Root, StringComparison.Ordinal)))
                    )),
                    Comparer<CallgraphsSection>.Create((a, b) => string.Compare(a.Project.Name, b.Project.Name, StringComparison.Ordinal))
                );
        }

        // Build sections list (Meta.Sections) from non-null non-empty collections (exclude Project itself)
        List<string> sectionNames = new();
        void AddIf<T>(IReadOnlyCollection<T>? list, string name) { if (list != null && list.Count > 0) sectionNames.Add(name); }
        AddIf(index.Tree, "Tree");
        AddIf(index.DI, "DI");
        AddIf(index.Entrypoints, "Entrypoints");
        AddIf(index.MessageFlow, "MessageFlow");
        AddIf(orderedCallgraphs, "Callgraphs");
        AddIf(index.Configs, "Configs");
        AddIf(index.Commands, "Commands");
        AddIf(index.CliCommands, "CliCommands");
        AddIf(index.Tests, "Tests");
        sectionNames.Sort(StringComparer.Ordinal);
        var meta = new Meta(index.Meta.Version, "1.2", index.Meta.GeneratedAt, index.Meta.RepositoryUrl, sectionNames.Count == 0 ? null : sectionNames);

        return new ProjectIndex(
            meta,
            orderedProjects,
            orderedTree.Count == 0 ? null : orderedTree,
            orderedDI.Count == 0 ? null : orderedDI,
            orderedEntrypoints.Count == 0 ? null : orderedEntrypoints,
            index.MessageFlow == null || index.MessageFlow.Count == 0 ? null : index.MessageFlow,
            orderedCallgraphs == null || orderedCallgraphs.Count == 0 ? null : orderedCallgraphs,
            orderedConfigs.Count == 0 ? null : orderedConfigs,
            index.Commands == null || index.Commands.Count == 0 ? null : index.Commands,
            index.CliCommands == null || index.CliCommands.Count == 0 ? null : index.CliCommands,
            index.Tests == null || index.Tests.Count == 0 ? null : index.Tests
        );
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // Use PascalCase (default)
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Add custom converters if needed
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
