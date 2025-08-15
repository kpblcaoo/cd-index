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
        var orderedTree = Orderer.Sort(
            index.Tree.Select(t => new TreeSection(
                Orderer.Sort(t.Files,
                    Comparer<FileEntry>.Create((x, y) =>
                        string.Compare(x.Path, y.Path, StringComparison.InvariantCulture))))),
            Comparer<TreeSection>.Create((x, y) => 0)); // TreeSections don't have a natural order yet

        // Order DI sections and registrations
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

        var orderedDI = Orderer.Sort(
            orderedDISections,
            Comparer<DISection>.Create((_, _) => 0));

        // Order entrypoints sections (by Project.Name then Project.File). HostedServices inside each already sorted below.
        var orderedEntrypoints = Orderer.Sort(
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
            }));

        // Order Config sections (each contains sorted lists already, but ensure deterministic ordering if multiple later)
        var orderedConfigs = Orderer.Sort(
            index.Configs.Select(c => new ConfigSection(
                c.EnvKeys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                c.AppProps.OrderBy(x => x, StringComparer.Ordinal).ToList()
            )),
            Comparer<ConfigSection>.Create((_, _) => 0));
        // Force Meta.SchemaVersion to 1.1 (immutability -> create new Meta)
        var meta = new Meta(index.Meta.Version, "1.1", index.Meta.GeneratedAt, index.Meta.RepositoryUrl);

        return new ProjectIndex(
            meta,
            orderedProjects,
            orderedTree,
            orderedDI,
            orderedEntrypoints,
            index.MessageFlow,
            // Order callgraphs: sections by Project.Name then graphs by Root then edges by Caller+Callee
            Orderer.Sort(index.Callgraphs.Select(sec => new CallgraphsSection(
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
            ),
            orderedConfigs,
            index.Commands,
            index.Tests
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
