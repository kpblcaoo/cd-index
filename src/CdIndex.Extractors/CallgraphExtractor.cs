using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class CallgraphExtractor : IExtractor
{
    private readonly List<string> _roots;
    private readonly int _maxDepth;
    private readonly int _maxNodes;
    private readonly bool _includeExternal;
    private readonly bool _verbose;
    private readonly Action<string>? _log;

    private readonly List<CallgraphsSection> _sections = new();

    public IReadOnlyList<CallgraphsSection> Sections => _sections;

    public CallgraphExtractor(IEnumerable<string> roots, int maxDepth, int maxNodes, bool includeExternal, bool verbose, Action<string>? log)
    {
        _roots = roots?.ToList() ?? new List<string>();
        _maxDepth = maxDepth;
        _maxNodes = maxNodes;
        _includeExternal = includeExternal;
        _verbose = verbose;
        _log = log;
    }

    public void Extract(RoslynContext context)
    {
        _sections.Clear();
        if (_roots.Count == 0) return; // nothing requested

        // Group roots by project for stable section grouping
        // For first iteration we search symbols globally then build per-project graphs.
        var projectGraphs = new Dictionary<Project, List<Callgraph>>();

        foreach (var project in context.Solution.Projects)
        {
            foreach (var rootId in _roots)
            {
                var (symbol, methodId, ambiguity) = ResolveMethod(project, rootId);
                if (symbol == null)
                {
                    if (_verbose) _log?.Invoke($"CLG100 root-not-found {project.Name} {rootId}");
                    continue;
                }
                if (ambiguity && _verbose) _log?.Invoke($"CLG110 ambiguous-root {project.Name} {rootId}");
                var graph = BuildGraph(context, symbol, methodId);
                if (!projectGraphs.TryGetValue(project, out var list))
                {
                    list = new List<Callgraph>();
                    projectGraphs[project] = list;
                }
                list.Add(graph);
            }
        }

        foreach (var kv in projectGraphs.OrderBy(k => k.Key.Name, StringComparer.Ordinal))
        {
            var proj = kv.Key;
            var fileFull = proj.FilePath ?? string.Empty;
            // repo-relative normalization consistent with other sections
            var repoRoot = context.RepoRoot;
            var fileRel = string.IsNullOrEmpty(fileFull) ? string.Empty : PathEx.Normalize(Path.GetFullPath(fileFull), Path.GetFullPath(repoRoot));
            var graphs = kv.Value
                .OrderBy(g => g.Root, StringComparer.Ordinal)
                .ToList();
            _sections.Add(new CallgraphsSection(new ProjectRef(proj.Name, fileRel), graphs));
        }
    }

    private (IMethodSymbol? symbol, string methodId, bool ambiguous) ResolveMethod(Project project, string rootSpec)
    {
        // rootSpec may end with /argCount
        int? specifiedArity = null;
        var spec = rootSpec;
        var slash = rootSpec.LastIndexOf('/');
        if (slash > 0 && int.TryParse(rootSpec.Substring(slash + 1), out var argCount))
        {
            specifiedArity = argCount;
            spec = rootSpec.Substring(0, slash);
        }
        // Expect Namespace.Type.Method or Namespace.Type..ctor
        var lastDot = spec.LastIndexOf('.');
        if (lastDot < 0) return (null, rootSpec, false);
        var typePart = spec.Substring(0, lastDot);
        var methodPart = spec.Substring(lastDot + 1);
        bool isCtor = methodPart == ".ctor" || methodPart == "..ctor";
        if (isCtor) methodPart = ".ctor";

        var matches = new List<IMethodSymbol>();
        foreach (var doc in project.Documents)
        {
            if (doc.SourceCodeKind != SourceCodeKind.Regular) continue;
            if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
            var root = doc.GetSyntaxRootAsync().Result;
            if (root == null) continue;
            var semanticModel = doc.GetSemanticModelAsync().Result;
            if (semanticModel == null) continue;
            var methodDecls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var m in methodDecls)
            {
                var symbol = semanticModel.GetDeclaredSymbol(m) as IMethodSymbol;
                if (symbol == null) continue;
                var containing = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (containing != typePart && containing != "global::" + typePart) continue;
                var name = symbol.Name;
                if (name != methodPart) continue;
                if (specifiedArity != null && symbol.Parameters.Length != specifiedArity.Value) continue;
                matches.Add(symbol);
            }
            if (isCtor)
            {
                var ctorDecls = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
                foreach (var c in ctorDecls)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(c) as IMethodSymbol;
                    if (symbol == null) continue;
                    var containing = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    if (containing != typePart && containing != "global::" + typePart) continue;
                    if (specifiedArity != null && symbol.Parameters.Length != specifiedArity.Value) continue;
                    matches.Add(symbol);
                }
            }
        }
        if (matches.Count == 0) return (null, rootSpec, false);
        // Build canonical method id from first match (disambiguate) -> TypeName.Method(argCount)
        var first = matches[0];
        var id = BuildMethodId(first);
        return (first, id, matches.Count > 1 && specifiedArity == null);
    }

    private Callgraph BuildGraph(RoslynContext ctx, IMethodSymbol root, string methodId)
    {
        var edges = new HashSet<(string Caller, string Callee)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(IMethodSymbol Symbol, int Depth)>();
        queue.Enqueue((root, 0));
        visited.Add(BuildMethodId(root));
        bool truncated = false;

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= _maxDepth) continue; // reached depth limit (edges at this depth already accounted by parent)

            foreach (var refEdge in FindCallees(current, ctx))
            {
                var callee = refEdge;
                var callerId = BuildMethodId(current);
                var calleeId = callee.Id;
                edges.Add((callerId, calleeId));
                if (callee.Symbol != null)
                {
                    if (!visited.Contains(callee.Id))
                    {
                        if (visited.Count >= _maxNodes)
                        {
                            truncated = true; break;
                        }
                        visited.Add(callee.Id);
                        if (depth + 1 <= _maxDepth - 1) // only enqueue if within depth limit
                            queue.Enqueue((callee.Symbol, depth + 1));
                    }
                }
            }
            if (truncated) break;
        }
        var orderedEdges = edges
            .OrderBy(e => e.Caller, StringComparer.Ordinal)
            .ThenBy(e => e.Callee, StringComparer.Ordinal)
            .Select(e => new CallEdge(e.Caller, e.Callee))
            .ToList();
        return new Callgraph(methodId, _maxDepth, truncated, orderedEdges);
    }

    private IEnumerable<(string Id, IMethodSymbol? Symbol)> FindCallees(IMethodSymbol method, RoslynContext ctx)
    {
        foreach (var decl in method.DeclaringSyntaxReferences)
        {
            var node = decl.GetSyntax();
            if (node is MethodDeclarationSyntax mds)
            {
                var semanticModel = ctx.Solution.GetDocument(mds.SyntaxTree)?.GetSemanticModelAsync().Result;
                if (semanticModel == null) continue;
                foreach (var invoke in mds.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var info = semanticModel.GetSymbolInfo(invoke);
                    var symbol = info.Symbol as IMethodSymbol;
                    if (symbol == null)
                    {
                        if (_includeExternal)
                        {
                            // attempt to get textual representation
                            var textId = invoke.Expression.ToString();
                            if (!string.IsNullOrWhiteSpace(textId))
                                yield return (textId, null);
                        }
                        continue;
                    }
                    if (symbol.Locations.Any(l => l.IsInSource))
                    {
                        yield return (BuildMethodId(symbol), symbol);
                    }
                    else if (_includeExternal)
                    {
                        yield return (BuildMethodId(symbol), null); // external leaf
                    }
                }
            }
            if (node is ConstructorDeclarationSyntax cds)
            {
                var semanticModel = ctx.Solution.GetDocument(cds.SyntaxTree)?.GetSemanticModelAsync().Result;
                if (semanticModel == null) continue;
                foreach (var invoke in cds.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var info = semanticModel.GetSymbolInfo(invoke);
                    var symbol = info.Symbol as IMethodSymbol;
                    if (symbol == null)
                    {
                        if (_includeExternal)
                        {
                            var textId = invoke.Expression.ToString();
                            if (!string.IsNullOrWhiteSpace(textId))
                                yield return (textId, null);
                        }
                        continue;
                    }
                    if (symbol.Locations.Any(l => l.IsInSource))
                    {
                        yield return (BuildMethodId(symbol), symbol);
                    }
                    else if (_includeExternal)
                    {
                        yield return (BuildMethodId(symbol), null);
                    }
                }
            }
        }
    }

    private static string BuildMethodId(IMethodSymbol symbol)
    {
        var typeName = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "<unknown>";
        var name = symbol.MethodKind == MethodKind.Constructor ? ".ctor" : symbol.Name;
        var paramCount = symbol.Parameters.Length;
        // Strip generic arity markers from type and method names
        typeName = StripGenerics(typeName);
        name = StripGenerics(name);
        return $"{typeName}.{name}({paramCount})";
    }

    private static string StripGenerics(string name)
    {
        // Remove `1 or similar
        var idx = name.IndexOf('`');
        if (idx > 0) name = name.Substring(0, idx);
        return name;
    }
}
