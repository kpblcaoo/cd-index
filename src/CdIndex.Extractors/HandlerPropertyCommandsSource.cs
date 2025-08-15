using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

internal sealed class HandlerPropertyCommandsSource
{
    private readonly Func<string, string?, string, int, bool> _add;

    public HandlerPropertyCommandsSource(Func<string, string?, string, int, bool> add) => _add = add;

    public void Collect(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (cls.Identifier.ValueText.Length == 0) continue;
            var clsSymbol = semantic.GetDeclaredSymbol(cls) as INamedTypeSymbol;
            if (clsSymbol == null) continue;
            bool looksLikeHandler = clsSymbol.AllInterfaces.Any(i => i.Name == "ICommandHandler") || clsSymbol.Name.EndsWith("CommandHandler", StringComparison.Ordinal);
            if (!looksLikeHandler) continue;
            foreach (var prop in cls.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (prop.Identifier.ValueText != "CommandName") continue;
                string? value = null;
                if (prop.ExpressionBody?.Expression is ExpressionSyntax expr1)
                {
                    value = TryGetRawCommandValue(expr1, semantic);
                }
                else if (prop.AccessorList != null)
                {
                    var getter = prop.AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                    if (getter != null)
                    {
                        if (getter.ExpressionBody?.Expression is ExpressionSyntax expr2)
                            value = TryGetRawCommandValue(expr2, semantic);
                        else if (getter.Body != null)
                        {
                            var ret = getter.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
                            if (ret?.Expression is ExpressionSyntax expr3)
                                value = TryGetRawCommandValue(expr3, semantic);
                        }
                    }
                }
                if (value == null) continue;
                var normalized = value.StartsWith('/') ? value : "/" + value;
                if (normalized.Length < 2 || normalized.Any(char.IsWhiteSpace)) continue;
                var (file, line) = LocationUtil.GetLocation(prop, ctx);
                _add(normalized, clsSymbol.Name, file, line);
            }
        }
    }

    private static string? TryGetRawCommandValue(ExpressionSyntax expr, SemanticModel semantic)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var v = lit.Token.ValueText.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            return v;
        }
        var constant = semantic.GetConstantValue(expr);
        if (constant.HasValue && constant.Value is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s.Trim();
        }
        return null;
    }
}
