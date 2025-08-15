using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

internal sealed class ComparisonCommandsSource
{
    private readonly Func<ExpressionSyntax, SemanticModel, string?> _tryGet;
    private readonly Func<string, string?, string, int, bool> _add;

    public ComparisonCommandsSource(Func<ExpressionSyntax, SemanticModel, string?> tryGet, Func<string, string?, string, int, bool> add)
    {
        _tryGet = tryGet;
        _add = add;
    }

    public void Collect(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        foreach (var bin in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!bin.IsKind(SyntaxKind.EqualsExpression)) continue;
            var left = _tryGet(bin.Left, semantic);
            var right = _tryGet(bin.Right, semantic);
            var cmd = left ?? right;
            if (cmd == null) continue;
            var (file, line) = LocationUtil.GetLocation(bin, ctx);
            _add(cmd, null, file, line);
        }

        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
            var id = ma.Name switch
            {
                GenericNameSyntax g => g.Identifier.ValueText,
                IdentifierNameSyntax idn => idn.Identifier.ValueText,
                _ => null
            };
            if (id is not ("Equals" or "StartsWith")) continue;
            if (inv.ArgumentList.Arguments.Count == 0) continue;
            var argExpr = inv.ArgumentList.Arguments[0].Expression;
            var cmd = _tryGet(argExpr, semantic);
            if (cmd == null) continue;
            var (file, line) = LocationUtil.GetLocation(inv, ctx);
            _add(cmd, null, file, line);
        }
    }
}
