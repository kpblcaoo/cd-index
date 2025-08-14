using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

namespace CdIndex.Extractors;

public sealed class ConfigExtractor : IExtractor
{
    private readonly HashSet<string> _envPrefixes;
    private readonly HashSet<string> _envKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _appProps = new(StringComparer.Ordinal);

    public ConfigExtractor(IEnumerable<string>? envPrefixes = null)
    {
        _envPrefixes = new HashSet<string>((envPrefixes ?? new[] { "DOORMAN_" }), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> EnvKeys => _envKeys;
    public IReadOnlyCollection<string> AppProps => _appProps;

    public void Extract(RoslynContext context)
    {
        _envKeys.Clear();
        _appProps.Clear();

        foreach (var project in context.Solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = doc.GetSyntaxRootAsync().Result as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = doc.GetSemanticModelAsync().Result;
                if (semantic == null) continue;

                CollectEnvLiterals(root);
                CollectAppProps(root, semantic);
            }
        }
    }

    private void CollectEnvLiterals(CSharpSyntaxNode root)
    {
        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (lit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
            {
                var text = lit.Token.ValueText;
                foreach (var prefix in _envPrefixes)
                {
                    if (text.StartsWith(prefix, StringComparison.Ordinal) && text.Length > prefix.Length)
                    {
                        _envKeys.Add(text);
                        break;
                    }
                }
            }
        }
    }

    private void CollectAppProps(CSharpSyntaxNode root, SemanticModel semantic)
    {
        foreach (var member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var symbol = semantic.GetSymbolInfo(member.Expression).Symbol;
            if (symbol == null) continue;
            var type = symbol switch
            {
                ILocalSymbol ls => ls.Type,
                IParameterSymbol ps => ps.Type,
                IPropertySymbol prs => prs.Type,
                IFieldSymbol fs => fs.Type,
                IMethodSymbol ms => ms.ReturnType,
                INamedTypeSymbol nts => nts,
                _ => null
            } as INamedTypeSymbol;
            if (type == null) continue;
            if (!IsAppConfigLike(type)) continue;
            var propName = member.Name.Identifier.ValueText;
            if (string.IsNullOrEmpty(propName)) continue;
            _appProps.Add(type.Name + "." + propName);
        }
    }

    private static bool IsAppConfigLike(INamedTypeSymbol type)
    {
        if (type.Name == "IAppConfig" || type.Name.EndsWith("Config", StringComparison.Ordinal)) return true;
        foreach (var i in type.AllInterfaces)
        {
            if (i.Name == "IAppConfig" || i.Name.EndsWith("Config", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public ConfigSection CreateSection()
    {
        var env = _envKeys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var props = _appProps.OrderBy(x => x, StringComparer.Ordinal).ToList();
        return new ConfigSection(env, props);
    }
}
