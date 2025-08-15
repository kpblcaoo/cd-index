using Microsoft.CodeAnalysis;
using System.Threading;

namespace CdIndex.Roslyn;

// Centralized synchronous wrappers to keep deterministic, single-threaded access patterns explicit.
// Avoid scattering .Result across extractors; future async migration can pivot here.
public static class RoslynSync
{
    public static Compilation? GetCompilation(Project project) => project.GetCompilationAsync(CancellationToken.None).GetAwaiter().GetResult();
    public static SyntaxNode? GetRoot(Document document) => document.GetSyntaxRootAsync(CancellationToken.None).GetAwaiter().GetResult();
    public static SemanticModel? GetModel(Document document) => document.GetSemanticModelAsync(CancellationToken.None).GetAwaiter().GetResult();
}
