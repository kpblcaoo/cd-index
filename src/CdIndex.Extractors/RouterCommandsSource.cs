using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

// Isolated logic for discovering router-based command registrations.
internal sealed class RouterCommandsSource
{
    private readonly HashSet<string> _routerNames;
    private readonly Func<string, string?, string, int, bool> _add; // (command, handler, file, line) => accepted

    public RouterCommandsSource(HashSet<string> routerNames, Func<string, string?, string, int, bool> add)
    {
        _routerNames = routerNames;
        _add = add;
    }

    public void Collect(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx, Func<ExpressionSyntax, SemanticModel, List<string>?> tryGetCommandTexts, Func<IdentifierNameSyntax, SemanticModel, string?> resolveIdentifierTypeName)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
            var name = ma.Name switch
            {
                GenericNameSyntax g => g.Identifier.ValueText,
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => null
            };
            if (name == null) continue;
            if (!_routerNames.Contains(name)) continue;
            if (inv.ArgumentList.Arguments.Count == 0) continue;
            var firstArgExpr = inv.ArgumentList.Arguments[0].Expression;
            var cmdTexts = tryGetCommandTexts(firstArgExpr, semantic);
            if (cmdTexts == null || cmdTexts.Count == 0) continue;
            string? handler = null;
            if (ma.Name is GenericNameSyntax gname && gname.TypeArgumentList.Arguments.Count == 1)
            {
                handler = gname.TypeArgumentList.Arguments[0].ToString();
            }
            else if (inv.ArgumentList.Arguments.Count > 1)
            {
                var second = inv.ArgumentList.Arguments[1].Expression;
                switch (second)
                {
                    case ObjectCreationExpressionSyntax oce:
                        handler = oce.Type.ToString();
                        break;
                    case IdentifierNameSyntax idn:
                        handler = resolveIdentifierTypeName(idn, semantic) ?? idn.Identifier.ValueText;
                        break;
                }
            }
            var (file, line) = LocationUtil.GetLocation(inv, ctx);
            foreach (var cmd in cmdTexts)
                _add(cmd, handler, file, line);
        }
    }
}
