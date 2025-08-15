using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class CommandsExtractor : IExtractor, IExtractor<CommandItem>
{
    private readonly List<CommandItem> _items = new();
    private readonly HashSet<(string, string?)> _seen = new();
    private readonly HashSet<string> _routerNames;
    private readonly HashSet<string> _attrNames;
    private readonly bool _caseInsensitive;
    private readonly bool _normalizeTrim;
    private readonly bool _normalizeEnsureSlash;
    private readonly bool _allowBare;
    private readonly Dictionary<string, List<CommandItem>> _canonicalGroups = new(StringComparer.Ordinal);
    private readonly List<CommandConflict> _conflicts = new();
    private readonly bool _includeComparisons;
    private readonly bool _includeRouter;
    private readonly bool _includeAttributes;
    private readonly System.Text.RegularExpressions.Regex? _allowRegex;

    public CommandsExtractor(IEnumerable<string>? routerNames = null, IEnumerable<string>? attrNames = null)
        : this(routerNames, attrNames, false, false, true, true, true, true, true, null) { }

    public CommandsExtractor(IEnumerable<string>? routerNames, IEnumerable<string>? attrNames, bool caseInsensitive, bool allowBare, bool normalizeTrim, bool normalizeEnsureSlash,
        bool includeRouter, bool includeAttributes, bool includeComparisons, string? allowRegex)
    {
        _routerNames = new HashSet<string>((routerNames ?? new[] { "Map", "Register", "Add", "On", "Route", "Bind" }), StringComparer.Ordinal);
        _attrNames = new HashSet<string>((attrNames ?? new[] { "Command", "Commands" }).Select(a => a.EndsWith("Attribute", StringComparison.Ordinal) ? a[..^9] : a), StringComparer.Ordinal);
        _caseInsensitive = caseInsensitive;
        _allowBare = allowBare || normalizeEnsureSlash;
        _normalizeTrim = normalizeTrim;
        _normalizeEnsureSlash = normalizeEnsureSlash;
        _includeRouter = includeRouter;
        _includeAttributes = includeAttributes;
        _includeComparisons = includeComparisons;
        _allowRegex = string.IsNullOrWhiteSpace(allowRegex) ? null : new System.Text.RegularExpressions.Regex(allowRegex!, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public IReadOnlyList<CommandItem> Items => _items;
    IReadOnlyList<CommandItem> IExtractor<CommandItem>.Items => _items;
    public IReadOnlyList<CommandConflict> Conflicts => _conflicts;

    public void Extract(RoslynContext context)
    {
        _items.Clear(); _seen.Clear(); _canonicalGroups.Clear(); _conflicts.Clear();
        foreach (var project in context.Solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                if (doc.GetSyntaxRootAsync().Result is not CSharpSyntaxNode root) continue;
                var semantic = doc.GetSemanticModelAsync().Result;
                if (semantic == null) continue;
                if (_includeRouter)
                    CollectRouterRegistrations(root, semantic, context);
                if (_includeComparisons)
                    CollectComparisons(root, semantic, context);
                if (_includeAttributes)
                {
                    CollectHandlerProperties(root, semantic, context);
                    CollectHandlerAttributes(root, semantic, context);
                }
            }
        }
        _items.Sort(static (a, b) =>
        {
            var c = string.Compare(a.Command, b.Command, StringComparison.Ordinal); if (c != 0) return c;
            c = string.Compare(a.Handler, b.Handler, StringComparison.Ordinal); if (c != 0) return c;
            c = string.Compare(a.File, b.File, StringComparison.Ordinal); if (c != 0) return c;
            return a.Line.CompareTo(b.Line);
        });
        if (_caseInsensitive)
        {
            foreach (var item in _items)
            {
                var key = item.Command.ToLowerInvariant();
                if (!_canonicalGroups.TryGetValue(key, out var list)) _canonicalGroups[key] = list = new List<CommandItem>();
                list.Add(item);
            }
            foreach (var kv in _canonicalGroups.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var distinct = kv.Value.Select(v => v.Command).Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count > 1)
                {
                    var variants = kv.Value.OrderBy(v => v.Command, StringComparer.Ordinal)
                        .ThenBy(v => v.Handler, StringComparer.Ordinal)
                        .ThenBy(v => v.File, StringComparer.Ordinal)
                        .ThenBy(v => v.Line).ToList();
                    _conflicts.Add(new CommandConflict(kv.Key, variants));
                }
            }
        }
    }

    private RouterCommandsSource? _routerSource;
    private void CollectRouterRegistrations(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        _routerSource ??= new RouterCommandsSource(_routerNames, (c, h, f, l) => { Add(c, h, f, l); return true; });
        _routerSource.Collect(root, semantic, ctx, TryGetCommandTexts, ResolveIdentifierTypeName);
    }

    private ComparisonCommandsSource? _comparisonSource;
    private void CollectComparisons(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        _comparisonSource ??= new ComparisonCommandsSource(
            (expr, sem) => TryGetCommandText(expr, sem, _allowBare),
            (c, h, f, l) => { Add(c, h, f, l); return true; });
        _comparisonSource.Collect(root, semantic, ctx);
    }

    private HandlerPropertyCommandsSource? _handlerPropertySource;
    private void CollectHandlerProperties(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        _handlerPropertySource ??= new HandlerPropertyCommandsSource((c, h, f, l) => { Add(c, h, f, l); return true; });
        _handlerPropertySource.Collect(root, semantic, ctx);
    }

    private HandlerAttributeCommandsSource? _handlerAttributeSource;
    private void CollectHandlerAttributes(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        _handlerAttributeSource ??= new HandlerAttributeCommandsSource(_attrNames, (c, h, f, l) => { Add(c, h, f, l); return true; });
        _handlerAttributeSource.Collect(root, semantic, ctx);
    }

    // (moved raw value & attribute constant helpers into dedicated source classes)

    private List<string>? TryGetCommandTexts(ExpressionSyntax expr, SemanticModel semantic)
    {
        var single = TryGetCommandText(expr, semantic, _allowBare); if (single != null) return new List<string> { single };
        if (expr is ImplicitArrayCreationExpressionSyntax iaces && iaces.Initializer != null) return ExtractFromInitializer(iaces.Initializer, semantic);
        if (expr is ArrayCreationExpressionSyntax aces && aces.Initializer != null) return ExtractFromInitializer(aces.Initializer, semantic);
        if (expr is IdentifierNameSyntax idn)
        {
            var sym = semantic.GetSymbolInfo(idn).Symbol;
            if (sym is ILocalSymbol ls)
                foreach (var r in ls.DeclaringSyntaxReferences)
                    if (r.GetSyntax() is VariableDeclaratorSyntax vds && vds.Initializer?.Value is ExpressionSyntax ve)
                    { var list = TryGetCommandTexts(ve, semantic); if (list != null && list.Count > 0) return list; }
                    else if (sym is IFieldSymbol fs)
                    {
                        foreach (var r2 in fs.DeclaringSyntaxReferences)
                        {
                            if (r2.GetSyntax() is VariableDeclaratorSyntax vds2 && vds2.Initializer?.Value is ExpressionSyntax ve2)
                            {
                                var list = TryGetCommandTexts(ve2, semantic);
                                if (list != null && list.Count > 0) return list;
                            }
                        }
                    }
        }
        return null;
    }

    private List<string>? ExtractFromInitializer(InitializerExpressionSyntax init, SemanticModel semantic)
    { var list = new List<string>(); foreach (var e in init.Expressions) { var s = TryGetCommandText(e, semantic, _allowBare); if (s != null) list.Add(s); } return list.Count > 0 ? list : null; }
    private string? TryGetCommandText(ExpressionSyntax expr, SemanticModel semantic, bool allowBare)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        { var v = lit.Token.ValueText; if (IsCommandLiteral(v)) return v; if (allowBare && v.Trim().Length > 0) return v; return null; }
        var constant = semantic.GetConstantValue(expr);
        if (constant.HasValue && constant.Value is string s && IsCommandLiteral(s)) return s;
        if (allowBare && constant.HasValue && constant.Value is string s2 && s2.Trim().Length > 0) return s2; return null;
    }
    private static string? ResolveIdentifierTypeName(IdentifierNameSyntax idn, SemanticModel semantic)
    { var sym = semantic.GetSymbolInfo(idn).Symbol; return sym switch { ILocalSymbol ls => ls.Type.Name, IFieldSymbol fs => fs.Type.Name, IPropertySymbol ps => ps.Type.Name, IParameterSymbol prs => prs.Type.Name, _ => null }; }
    private static bool IsCommandLiteral(string s) => CommandText.IsCommandLiteral(s);
    private void Add(string command, string? handler, string file, int line)
    {
        var norm = CommandText.Normalize(command, _normalizeTrim, _normalizeEnsureSlash);
        if (norm == null) return; if (_allowRegex != null && !_allowRegex.IsMatch(norm)) return;
        if (!_seen.Add((norm, handler))) return; _items.Add(new CommandItem(norm, handler, file, line));
    }
    private void RecordCanonical(CommandItem item) { }
}

public sealed record CommandConflict(string CanonicalCommand, IReadOnlyList<CommandItem> Variants);
