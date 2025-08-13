using Microsoft.CodeAnalysis;
using System.IO;

namespace CdIndex.Roslyn;

public static class LocationUtil
{
    public static (string Path, int Line) GetLocation(SyntaxNode node, RoslynContext ctx)
    {
        var span = node.GetLocation().GetLineSpan();
        var absPath = span.Path.Replace("\\", "/");
        var relPath = absPath.Replace(ctx.RepoRoot.Replace("\\", "/"), "").TrimStart('/');
        var line = span.StartLinePosition.Line + 1;
        return (relPath, line);
    }
}
