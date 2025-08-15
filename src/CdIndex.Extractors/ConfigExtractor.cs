using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;

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

    private static readonly Regex EnvKeyRegex = new("^[A-Z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                        // Only accept plausible env var tokens (all-caps, digits, underscore)
                        if (EnvKeyRegex.IsMatch(text))
                        {
                            _envKeys.Add(text);
                        }
                        break; // don't test other prefixes
                    }
                }
            }
        }
    }

    private void CollectAppProps(CSharpSyntaxNode root, SemanticModel semantic)
    {
        foreach (var member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            // Only consider property accesses (exclude method invocations / GetType etc)
            var accessedSymbol = semantic.GetSymbolInfo(member).Symbol as IPropertySymbol;
            if (accessedSymbol == null) continue;
            var containingType = accessedSymbol.ContainingType;
            if (containingType == null) continue;
            if (!IsAppConfigLike(containingType)) continue;
            var propName = accessedSymbol.Name;
            if (string.IsNullOrEmpty(propName)) continue;
            _appProps.Add(containingType.Name + "." + propName);
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
