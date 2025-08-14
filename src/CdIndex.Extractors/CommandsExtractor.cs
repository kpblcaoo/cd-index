using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class CommandsExtractor : IExtractor
{
    private readonly List<CommandItem> _items = new();
    private readonly HashSet<(string,string?,string,int)> _dedup = new();

    public IReadOnlyList<CommandItem> Items => _items;

    public void Extract(RoslynContext context)
    {
        _items.Clear();
        _dedup.Clear();
        foreach (var project in context.Solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = doc.GetSyntaxRootAsync().Result as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = doc.GetSemanticModelAsync().Result;
                if (semantic == null) continue;

                CollectRouterRegistrations(root, semantic, context);
                CollectComparisons(root, semantic, context);
            }
        }
        _items.Sort((a,b) =>
        {
            var c = string.Compare(a.Command, b.Command, StringComparison.Ordinal);
            if (c != 0) return c;
            return string.Compare(a.Handler, b.Handler, StringComparison.Ordinal);
        });
    }

    private void CollectRouterRegistrations(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
            var name = ma.Name switch
            {
                GenericNameSyntax g => g.Identifier.ValueText,
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => null
            };
            if (name == null) continue;
            if (!IsRouterRegistrationName(name)) continue;
            if (inv.ArgumentList.Arguments.Count == 0) continue;
            var firstArg = inv.ArgumentList.Arguments[0].Expression;
            var literal = firstArg as LiteralExpressionSyntax;
            if (literal?.IsKind(SyntaxKind.StringLiteralExpression) != true) continue;
            var cmdText = literal.Token.ValueText;
            if (!IsCommandLiteral(cmdText)) continue;
            string? handler = null;
            // Generic handler type parameter
            if (ma.Name is GenericNameSyntax gname && gname.TypeArgumentList.Arguments.Count == 1)
            {
                handler = gname.TypeArgumentList.Arguments[0].ToString();
            }
            else if (inv.ArgumentList.Arguments.Count > 1)
            {
                // second arg maybe new Handler()
                var second = inv.ArgumentList.Arguments[1].Expression;
                if (second is ObjectCreationExpressionSyntax oce)
                {
                    handler = oce.Type.ToString();
                }
                else if (second is IdentifierNameSyntax idn)
                {
                    handler = idn.Identifier.ValueText;
                }
            }
            else
            {
                // maybe first arg is string and second implicit new Handler() with collection initializer? skip
            }

            var (file,line) = LocationUtil.GetLocation(inv, ctx);
            Add(cmdText, handler, file, line);
        }
    }

    private void CollectComparisons(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        foreach (var bin in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!bin.IsKind(SyntaxKind.EqualsExpression)) continue;
            var leftLit = bin.Left as LiteralExpressionSyntax;
            var rightLit = bin.Right as LiteralExpressionSyntax;
            var lit = leftLit ?? rightLit;
            if (lit?.IsKind(SyntaxKind.StringLiteralExpression) != true) continue;
            var text = lit.Token.ValueText;
            if (!IsCommandLiteral(text)) continue;
            var (file,line) = LocationUtil.GetLocation(bin, ctx);
            Add(text, null, file, line);
        }

        // method invocations like text.Equals("/help") or text.StartsWith("/ban")
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
            var id = ma.Name.Identifier.ValueText;
            if (id is not ("Equals" or "StartsWith")) continue;
            if (inv.ArgumentList.Arguments.Count == 0) continue;
            var arg = inv.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax;
            if (arg?.IsKind(SyntaxKind.StringLiteralExpression) != true) continue;
            var text = arg.Token.ValueText;
            if (!IsCommandLiteral(text)) continue;
            var (file,line) = LocationUtil.GetLocation(inv, ctx);
            Add(text, null, file, line);
        }
    }

    private static bool IsCommandLiteral(string s) => s.Length > 1 && s[0] == '/';
    private static bool IsRouterRegistrationName(string name) => name is "Map" or "Register" or "Add" or "On" or "Handle";

    private void Add(string command, string? handler, string file, int line)
    {
        var key = (command, handler, file, line);
        if (_dedup.Add(key))
            _items.Add(new CommandItem(command, handler, file, line));
    }
}
