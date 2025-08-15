using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

internal sealed class HandlerAttributeCommandsSource
{
    private readonly HashSet<string> _attrNames;
    private readonly Func<string, string?, string, int, bool> _add;

    public HandlerAttributeCommandsSource(HashSet<string> attrNames, Func<string, string?, string, int, bool> add)
    {
        _attrNames = attrNames;
        _add = add;
    }

    public void Collect(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var clsSymbol = semantic.GetDeclaredSymbol(cls) as INamedTypeSymbol;
            if (clsSymbol == null) continue;
            foreach (var attr in clsSymbol.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName == null) continue;
                if (attrName.EndsWith("Attribute", StringComparison.Ordinal)) attrName = attrName[..^9];
                if (!_attrNames.Contains(attrName)) continue;
                var values = new List<string>();
                foreach (var ca in attr.ConstructorArguments) CollectAttributeTypedConstant(ca, values);
                foreach (var na in attr.NamedArguments) CollectAttributeTypedConstant(na.Value, values);
                if (values.Count == 0) continue;
                var (file, line) = LocationUtil.GetLocation(cls, ctx);
                foreach (var raw in values)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var trimmed = raw.Trim();
                    if (!trimmed.StartsWith('/')) trimmed = "/" + trimmed;
                    if (trimmed.Length < 2 || trimmed.Any(char.IsWhiteSpace)) continue;
                    _add(trimmed, clsSymbol.Name, file, line);
                }
            }
        }
    }

    private static void CollectAttributeTypedConstant(TypedConstant tc, List<string> sink)
    {
        if (tc.Kind == TypedConstantKind.Array)
        {
            foreach (var v in tc.Values) CollectAttributeTypedConstant(v, sink);
            return;
        }
        if (tc.Value is string s) sink.Add(s);
    }
}
