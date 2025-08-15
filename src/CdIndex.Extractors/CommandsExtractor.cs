using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class CommandsExtractor : IExtractor
{
    private readonly List<CommandItem> _items = new();
    // Dedup uniqueness only on (Command, Handler) per requirements
    private readonly HashSet<(string, string?)> _seen = new();
    private readonly HashSet<string> _routerNames; // configurable list
    private readonly HashSet<string> _attrNames; // attribute simple names (without Attribute suffix)
    private readonly bool _caseInsensitive;
    private readonly bool _normalizeTrim;
    private readonly bool _normalizeEnsureSlash;
    private readonly bool _allowBare;
    private readonly Dictionary<string, List<CommandItem>> _canonicalGroups = new(StringComparer.Ordinal); // key: lower(command)
    private readonly List<CommandConflict> _conflicts = new();
    private readonly bool _includeComparisons;
    private readonly bool _includeRouter;
    private readonly bool _includeAttributes;
    private readonly System.Text.RegularExpressions.Regex? _allowRegex;

    public CommandsExtractor(IEnumerable<string>? routerNames = null, IEnumerable<string>? attrNames = null)
        : this(routerNames, attrNames, caseInsensitive: false, allowBare: false, normalizeTrim: true, normalizeEnsureSlash: true, includeRouter: true, includeAttributes: true, includeComparisons: true, allowRegex: null) { }

    public CommandsExtractor(IEnumerable<string>? routerNames, IEnumerable<string>? attrNames, bool caseInsensitive, bool allowBare, bool normalizeTrim, bool normalizeEnsureSlash,
        bool includeRouter, bool includeAttributes, bool includeComparisons, string? allowRegex)
    {
        _routerNames = new HashSet<string>((routerNames ?? new[] { "Map", "Register", "Add", "On", "Route", "Bind" }), StringComparer.Ordinal);
        _attrNames = new HashSet<string>((attrNames ?? new[] { "Command", "Commands" }).Select(a => a.EndsWith("Attribute", StringComparison.Ordinal) ? a[..^9] : a), StringComparer.Ordinal);
        _caseInsensitive = caseInsensitive;
        _allowBare = allowBare || normalizeEnsureSlash; // we can accept bare if we'll add slash
        _normalizeTrim = normalizeTrim;
        _normalizeEnsureSlash = normalizeEnsureSlash;
        _includeRouter = includeRouter;
        _includeAttributes = includeAttributes;
        _includeComparisons = includeComparisons;
        _allowRegex = string.IsNullOrWhiteSpace(allowRegex) ? null : new System.Text.RegularExpressions.Regex(allowRegex!, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public IReadOnlyList<CommandItem> Items => _items;
    public IReadOnlyList<CommandConflict> Conflicts => _conflicts;

    public void Extract(RoslynContext context)
    {
    _items.Clear();
    _seen.Clear();
    _canonicalGroups.Clear();
    _conflicts.Clear();
        foreach (var project in context.Solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = doc.GetSyntaxRootAsync().Result as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = doc.GetSemanticModelAsync().Result;
                if (semantic == null) continue;

                if (_includeRouter)
                    CollectRouterRegistrations(root, semantic, context);
                if (_includeComparisons)
                    CollectComparisons(root, semantic, context);
                if (_includeAttributes)
                {
                    CollectHandlerProperties(root, semantic, context); // pattern: property CommandName => "start"
                    CollectHandlerAttributes(root, semantic, context); // pattern: [Command("/start"), Commands("/a","/b")]
                }
            }
        }
        _items.Sort(static (a, b) =>
        {
            var c = string.Compare(a.Command, b.Command, StringComparison.Ordinal);
            if (c != 0) return c;
            c = string.Compare(a.Handler, b.Handler, StringComparison.Ordinal);
            if (c != 0) return c;
            c = string.Compare(a.File, b.File, StringComparison.Ordinal);
            if (c != 0) return c;
            return a.Line.CompareTo(b.Line);
        });
        if (_caseInsensitive)
        {
            // Build groups by lower(command)
            foreach (var item in _items)
            {
                var key = item.Command.ToLowerInvariant();
                if (!_canonicalGroups.TryGetValue(key, out var list))
                {
                    list = new List<CommandItem>();
                    _canonicalGroups[key] = list;
                }
                list.Add(item);
            }
            foreach (var kv in _canonicalGroups.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var distinctForms = kv.Value.Select(v => v.Command).Distinct(StringComparer.Ordinal).ToList();
                if (distinctForms.Count > 1)
                {
                    var variants = kv.Value
                        .OrderBy(v => v.Command, StringComparer.Ordinal)
                        .ThenBy(v => v.Handler, StringComparer.Ordinal)
                        .ThenBy(v => v.File, StringComparer.Ordinal)
                        .ThenBy(v => v.Line)
                        .ToList();
                    _conflicts.Add(new CommandConflict(kv.Key, variants));
                }
            }
        }
    }

    private void CollectRouterRegistrations(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
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
            var cmdTexts = TryGetCommandTexts(firstArgExpr, semantic);
            if (cmdTexts == null || cmdTexts.Count == 0) continue;
            string? handler = null;
            // Generic type parameter overrides other detection
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
                        handler = ResolveIdentifierTypeName(idn, semantic) ?? idn.Identifier.ValueText;
                        break;
                }
            }
            var (file, line) = LocationUtil.GetLocation(inv, ctx);
            foreach (var cmd in cmdTexts)
                Add(cmd, handler, file, line);
        }
    }

    private void CollectComparisons(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        foreach (var bin in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!bin.IsKind(SyntaxKind.EqualsExpression)) continue;
            var left = TryGetCommandText(bin.Left, semantic, allowBare: _allowBare);
            var right = TryGetCommandText(bin.Right, semantic, allowBare: _allowBare);
            var cmd = left ?? right;
            if (cmd == null) continue;
            var (file, line) = LocationUtil.GetLocation(bin, ctx);
            Add(cmd, null, file, line);
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
            var cmd = TryGetCommandText(argExpr, semantic, allowBare: _allowBare);
            if (cmd == null) continue;
            var (file, line) = LocationUtil.GetLocation(inv, ctx);
            Add(cmd, null, file, line);
        }
    }

    private void CollectHandlerProperties(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        // Detect classes that implement an interface named ICommandHandler (any namespace) OR class name ends with CommandHandler
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (cls.Identifier.ValueText.Length == 0) continue;
            var clsSymbol = semantic.GetDeclaredSymbol(cls) as INamedTypeSymbol;
            if (clsSymbol == null) continue;
            bool looksLikeHandler = clsSymbol.AllInterfaces.Any(i => i.Name == "ICommandHandler") || clsSymbol.Name.EndsWith("CommandHandler", StringComparison.Ordinal);
            if (!looksLikeHandler) continue;
            // Find property named CommandName (common pattern). Could extend via configuration later.
            foreach (var prop in cls.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (prop.Identifier.ValueText != "CommandName") continue;
                string? value = null;
                // expression-bodied property
                if (prop.ExpressionBody?.Expression is ExpressionSyntax expr1)
                {
                    value = TryGetRawCommandValue(expr1, semantic);
                }
                else if (prop.AccessorList != null)
                {
                    // look for a get accessor with single return statement of literal/const
                    var getter = prop.AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                    if (getter != null)
                    {
                        if (getter.ExpressionBody?.Expression is ExpressionSyntax expr2)
                            value = TryGetRawCommandValue(expr2, semantic);
                        else if (getter.Body != null)
                        {
                            // scan return statements
                            var ret = getter.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
                            if (ret?.Expression is ExpressionSyntax expr3)
                                value = TryGetRawCommandValue(expr3, semantic);
                        }
                    }
                }
                if (value == null) continue;
                // Normalize: ensure leading slash
                var normalized = value.StartsWith('/') ? value : "/" + value;
                // Reject if contains whitespace or is just "/"
                if (normalized.Length < 2 || normalized.Any(char.IsWhiteSpace)) continue;
                var (file, line) = LocationUtil.GetLocation(prop, ctx);
                Add(normalized, clsSymbol.Name, file, line);
            }
        }
    }

    private void CollectHandlerAttributes(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
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
                // Collect constructor args
                var values = new List<string>();
                foreach (var ca in attr.ConstructorArguments)
                {
                    CollectAttributeTypedConstant(ca, values);
                }
                foreach (var na in attr.NamedArguments)
                {
                    CollectAttributeTypedConstant(na.Value, values);
                }
                if (values.Count == 0) continue;
                var (file, line) = LocationUtil.GetLocation(cls, ctx);
                foreach (var raw in values)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var trimmed = raw.Trim();
                    if (!trimmed.StartsWith('/')) trimmed = "/" + trimmed;
                    if (trimmed.Length < 2 || trimmed.Any(char.IsWhiteSpace)) continue;
                    Add(trimmed, clsSymbol.Name, file, line);
                }
            }
        }
    }

    private static void CollectAttributeTypedConstant(TypedConstant tc, List<string> sink)
    {
        if (tc.Kind == TypedConstantKind.Array)
        {
            foreach (var v in tc.Values)
                CollectAttributeTypedConstant(v, sink);
            return;
        }
        if (tc.Value is string s) sink.Add(s);
    }

    private static string? TryGetRawCommandValue(ExpressionSyntax expr, SemanticModel semantic)
    {
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var v = lit.Token.ValueText.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            return v.StartsWith('/') ? v : v; // add slash later
        }
        var constant = semantic.GetConstantValue(expr);
        if (constant.HasValue && constant.Value is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s.Trim();
        }
        return null;
    }

    private List<string>? TryGetCommandTexts(ExpressionSyntax expr, SemanticModel semantic)
    {
        // Single literal / constant
        var single = TryGetCommandText(expr, semantic, allowBare: _allowBare);
        if (single != null) return new List<string> { single };

        // Direct inline array creation: new[] { "/a", "/b" }
        if (expr is ImplicitArrayCreationExpressionSyntax iaces && iaces.Initializer != null)
            return ExtractFromInitializer(iaces.Initializer, semantic);
        if (expr is ArrayCreationExpressionSyntax aces && aces.Initializer != null)
            return ExtractFromInitializer(aces.Initializer, semantic);

        // Identifier referencing array variable with initializer
        if (expr is IdentifierNameSyntax idn)
        {
            var sym = semantic.GetSymbolInfo(idn).Symbol;
            if (sym is ILocalSymbol ls && ls.DeclaringSyntaxReferences.Length > 0)
            {
                foreach (var r in ls.DeclaringSyntaxReferences)
                {
                    if (r.GetSyntax() is VariableDeclaratorSyntax vds && vds.Initializer?.Value is ExpressionSyntax ve)
                    {
                        var list = TryGetCommandTexts(ve, semantic);
                        if (list != null && list.Count > 0) return list;
                    }
                }
            }
            else if (sym is IFieldSymbol fs && fs.DeclaringSyntaxReferences.Length > 0)
            {
                foreach (var r in fs.DeclaringSyntaxReferences)
                {
                    if (r.GetSyntax() is VariableDeclaratorSyntax vds && vds.Initializer?.Value is ExpressionSyntax ve)
                    {
                        var list = TryGetCommandTexts(ve, semantic);
                        if (list != null && list.Count > 0) return list;
                    }
                }
            }
        }

        return null;
    }

    private List<string>? ExtractFromInitializer(InitializerExpressionSyntax init, SemanticModel semantic)
    {
        var list = new List<string>();
        foreach (var e in init.Expressions)
        {
            var s = TryGetCommandText(e, semantic, allowBare: _allowBare);
            if (s != null) list.Add(s);
        }
        return list.Count > 0 ? list : null;
    }

    private string? TryGetCommandText(ExpressionSyntax expr, SemanticModel semantic, bool allowBare)
    {
        // Direct literal
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var v = lit.Token.ValueText;
            if (IsCommandLiteral(v)) return v;
            if (allowBare && v.Trim().Length > 0) return v; // will normalize later
            return null;
        }
        // Constant value resolution
        var constant = semantic.GetConstantValue(expr);
        if (constant.HasValue && constant.Value is string s && IsCommandLiteral(s))
        {
            return s;
        }
    if (allowBare && constant.HasValue && constant.Value is string s2 && s2.Trim().Length > 0) return s2;
        return null;
    }

    private static string? ResolveIdentifierTypeName(IdentifierNameSyntax idn, SemanticModel semantic)
    {
        var sym = semantic.GetSymbolInfo(idn).Symbol;
        if (sym is ILocalSymbol ls) return ls.Type.Name;
        if (sym is IFieldSymbol fs) return fs.Type.Name;
        if (sym is IPropertySymbol ps) return ps.Type.Name;
        if (sym is IParameterSymbol prs) return prs.Type.Name;
        return null;
    }

    private static bool IsCommandLiteral(string s) => s.Length > 1 && s[0] == '/';

    private void Add(string command, string? handler, string file, int line)
    {
        var norm = Normalize(command);
        if (norm == null) return;
    if (_allowRegex != null && !_allowRegex.IsMatch(norm)) return; // regex gate
    if (!_seen.Add((norm, handler))) return; // dedupe (command, handler)
        var item = new CommandItem(norm, handler, file, line);
        _items.Add(item);
    // defer conflict grouping until end
    }

    private string? Normalize(string? raw)
    {
        if (raw == null) return null;
        var s = raw;
        if (_normalizeTrim) s = s.Trim();
        if (s.Length == 0) return null;
        if (_normalizeEnsureSlash && !s.StartsWith('/')) s = "/" + s;
        if (s.Length < 2 || s.Any(char.IsWhiteSpace)) return null;
        return s;
    }

    private void RecordCanonical(CommandItem item) { /* deprecated path kept for compatibility; now grouping done post-sort */ }
}

public sealed record CommandConflict(string CanonicalCommand, IReadOnlyList<CommandItem> Variants);
