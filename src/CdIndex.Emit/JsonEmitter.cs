using System.Text.Json;
using System.Text.Json.Serialization;
using CdIndex.Core;

namespace CdIndex.Emit;

public static class JsonEmitter
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    public static void Emit(ProjectIndex projectIndex, Stream stream)
    {
        using var _ = InvariantCultureScope.Enter();

        // Order all collections for deterministic output
        var orderedIndex = OrderCollections(projectIndex);

        JsonSerializer.Serialize(stream, orderedIndex, s_options);
    }

    public static string EmitString(ProjectIndex projectIndex)
    {
        using var stream = new MemoryStream();
        Emit(projectIndex, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ProjectIndex OrderCollections(ProjectIndex index)
    {
        // Order project sections
        var orderedProjects = Orderer.Sort(
            index.Project,
            Comparer<ProjectSection>.Create((x, y) =>
                string.Compare(x.Name, y.Name, StringComparison.InvariantCulture)));

        // Order tree sections and their files
        var orderedTree = Orderer.Sort(
            index.Tree.Select(t => new TreeSection(
                Orderer.Sort(t.Files,
                    Comparer<FileEntry>.Create((x, y) =>
                        string.Compare(x.Path, y.Path, StringComparison.InvariantCulture))))),
            Comparer<TreeSection>.Create((x, y) => 0)); // TreeSections don't have a natural order yet

        // Order DI sections and registrations
        var orderedDI = Orderer.Sort(
            index.DI.Select(di => new DISection(
                Orderer.Sort(di.Registrations,
                    Comparer<DIEntry>.Create((x, y) =>
                        string.Compare(x.ServiceType, y.ServiceType, StringComparison.InvariantCulture))))),
            Comparer<DISection>.Create((x, y) => 0));

        // Order entrypoint sections
        var orderedEntrypoints = Orderer.Sort(
            index.Entrypoints.Select(ep => new EntrypointSection(
                Orderer.Sort(ep.Entrypoints,
                    Comparer<EntrypointEntry>.Create((x, y) =>
                        string.Compare(x.Name, y.Name, StringComparison.InvariantCulture))))),
            Comparer<EntrypointSection>.Create((x, y) => 0));

        return new ProjectIndex(
            index.Meta,
            orderedProjects,
            orderedTree,
            orderedDI,
            orderedEntrypoints,
            index.MessageFlow,
            index.Callgraphs,
            index.Configs,
            index.Commands,
            index.Tests
        );
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // Use PascalCase (default)
            WriteIndented = false, // Compact output
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Add custom converters if needed
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
