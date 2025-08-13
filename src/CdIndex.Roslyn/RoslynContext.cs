using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CdIndex.Roslyn;

public sealed class RoslynContext
{
    public MSBuildWorkspace Workspace { get; }
    public Solution Solution { get; }
    public IReadOnlyDictionary<ProjectId, Compilation> Compilations { get; }
    public string RepoRoot { get; }

    public RoslynContext(
        MSBuildWorkspace workspace,
        Solution solution,
        IReadOnlyDictionary<ProjectId, Compilation> compilations,
        string repoRoot)
    {
        Workspace = workspace;
        Solution = solution;
        Compilations = compilations;
        RepoRoot = repoRoot;
    }

    public Document? FindDocument(SyntaxTree tree)
    {
        foreach (var project in Solution.Projects)
        {
            var doc = project.Documents.FirstOrDefault(d => d.TryGetSyntaxTree(out var t) && t == tree);
            if (doc is not null) return doc;
        }
        return null;
    }

    public async Task<SemanticModel> GetSemanticModelAsync(Document doc, CancellationToken ct)
    {
        if (!doc.SupportsSyntaxTree)
            throw new InvalidOperationException($"Document '{doc.FilePath ?? doc.Name}' does not support a syntax tree.");

        if (!doc.Project.SupportsCompilation)
            throw new InvalidOperationException($"Project '{doc.Project.Name}' does not support compilation.");

        var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model is null)
            throw new InvalidOperationException($"Failed to obtain SemanticModel for '{doc.FilePath ?? doc.Name}'.");

        return model;
    }
}
