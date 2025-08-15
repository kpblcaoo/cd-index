using Microsoft.CodeAnalysis;
using CdIndex.Core;

namespace CdIndex.Roslyn;

public static class LocationUtil
{
    public static (string Path, int Line) GetLocation(SyntaxNode node, RoslynContext ctx)
    {
        var span = node.GetLocation().GetLineSpan();
        var absPath = span.Path.Replace("\\", "/");
        var relPath = PathEx.Normalize(absPath, ctx.RepoRoot);
        var line = span.StartLinePosition.Line + 1;
        return (relPath, line);
    }
}
