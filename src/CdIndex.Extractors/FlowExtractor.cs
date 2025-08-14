using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class FlowExtractor : IExtractor
{
    private readonly string _handlerTypeInput;
    private readonly string _methodName;
    private readonly List<FlowNode> _nodes = new();
    private readonly bool _verbose;
    private readonly Action<string>? _log;

    public FlowExtractor(string handlerType, string methodName = "HandleAsync", bool verbose = false, Action<string>? log = null)
    {
        _handlerTypeInput = handlerType;
        _methodName = methodName;
        _verbose = verbose;
        _log = log;
    }

    public IReadOnlyList<FlowNode> Nodes => _nodes;

    public void Extract(RoslynContext context)
    {
        _nodes.Clear();

        // Collect candidate class symbols (simple pass over syntax for performance determinism)
        var candidates = new List<(INamedTypeSymbol Symbol, ClassDeclarationSyntax Decl, SemanticModel Model)>();
        var lastIdentifier = _handlerTypeInput.Contains('.') ? _handlerTypeInput.Split('.').Last() : _handlerTypeInput;

        foreach (var project in context.Solution.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            Compilation? comp = null;
            try { comp = project.GetCompilationAsync().Result; } catch { }
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = doc.GetSyntaxRootAsync().Result as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = doc.GetSemanticModelAsync().Result;
                if (semantic == null) continue;
                foreach (var decl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (decl.Identifier.ValueText != lastIdentifier) continue;
                    var symbol = semantic.GetDeclaredSymbol(decl) as INamedTypeSymbol;
                    if (symbol == null) continue;
                    candidates.Add((symbol, decl, semantic));
                }
            }
        }

        // If input contains namespace, attempt direct metadata resolve first
        INamedTypeSymbol? chosen = null;
        ClassDeclarationSyntax? chosenDecl = null;
        SemanticModel? chosenModel = null;

        if (_handlerTypeInput.Contains('.'))
        {
            foreach (var project in context.Solution.Projects)
            {
                var comp = project.GetCompilationAsync().Result;
                var sym = comp?.GetTypeByMetadataName(_handlerTypeInput);
                if (sym != null)
                {
                    // Need corresponding syntax (first DeclaringSyntaxReferences)
                    var syntaxRef = sym.DeclaringSyntaxReferences.FirstOrDefault();
                    if (syntaxRef != null)
                    {
                        var declNode = syntaxRef.GetSyntax() as ClassDeclarationSyntax;
                        if (declNode != null)
                        {
                            var doc = project.Documents.FirstOrDefault(d => d.GetSyntaxRootAsync().Result == declNode.SyntaxTree.GetRoot());
                            var model = doc?.GetSemanticModelAsync().Result;
                            if (model != null)
                            {
                                chosen = sym; chosenDecl = declNode; chosenModel = model; break;
                            }
                        }
                    }
                }
            }
        }

        if (chosen == null)
        {
            // Fallback: pick by simple name deterministically
            var orderedCandidates = candidates
                .OrderBy(c => c.Symbol.ContainingNamespace == null || c.Symbol.ContainingNamespace.IsGlobalNamespace ? 0 : 1)
                .ThenBy(c => c.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
                .ToList();
            (chosen, chosenDecl, chosenModel) = orderedCandidates.FirstOrDefault();
        }

        if (chosen == null || chosenDecl == null || chosenModel == null)
            throw new InvalidOperationException($"flow handler type not found: {_handlerTypeInput}");

        _logVerbose($"[flow] type: {chosen.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}");

        // Find method by name & return type (void/Task/ValueTask)
        var methodSymbols = chosen.GetMembers().OfType<IMethodSymbol>()
            .Where(m => string.Equals(m.Name, _methodName, StringComparison.Ordinal))
            .Where(m => IsAllowedReturn(m.ReturnType))
            .OrderBy(m => m.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
            .ToList();
        if (methodSymbols.Count == 0)
            throw new InvalidOperationException($"flow method not found: {_methodName} in type {chosen.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}");
        var methodSymbol = methodSymbols.First();
        _logVerbose($"[flow] method: {methodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}");

        // Acquire syntax of chosen method
        var methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        if (methodSyntax == null)
            throw new InvalidOperationException("flow method syntax not found (partial or metadata-only)");

    ProcessMethod(methodSyntax, context);

        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            _nodes[i] = n with { Order = i };
        }

        if (_nodes.Count == 0)
            _logVerbose("[flow] 0 nodes; nothing matched top-level patterns");
        else
            _logVerbose($"[flow] nodes: {_nodes.Count}");
    }

    private static bool IsAllowedReturn(ITypeSymbol t)
    {
        if (t == null) return false;
        var name = t.Name;
        if (name == "Void") return true;
        if (name == "Task" || name == "ValueTask") return true;
        return false;
    }

    private void ProcessMethod(MethodDeclarationSyntax method, RoslynContext ctx)
    {
        if (method.Body == null) return; // expression-bodied not handled now
        foreach (var stmt in method.Body.Statements)
        {
            switch (stmt)
            {
                case IfStatementSyntax ifs:
                    HandleIf(ifs, ctx);
                    break;
                case ExpressionStatementSyntax es:
                    HandleExpressionStatement(es, ctx);
                    break;
                case ReturnStatementSyntax rs:
                    AddNode("return", "return", rs, ctx);
                    break;
            }
        }
    }

    private void HandleIf(IfStatementSyntax ifs, RoslynContext ctx)
    {
        // Collapse pattern: if (Cond()) { Facade.Handle(); return; } -> delegate only
        if (ifs.Statement is BlockSyntax blk && blk.Statements.Count == 2 &&
            blk.Statements[0] is ExpressionStatementSyntax firstExpr &&
            blk.Statements[1] is ReturnStatementSyntax)
        {
            if (TryDelegateInvocation(firstExpr.Expression, ctx)) return; // collapsed
        }

        // Standard guard (never emit inner returns as nodes to preserve legacy semantics)
        AddNode("guard", ifs.Condition.ToString(), ifs, ctx);

        if (ifs.Statement is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
            {
                if (inner is ExpressionStatementSyntax es)
                    TryDelegateInvocation(es.Expression, ctx);
                // return inside block ignored
            }
        }
        else if (ifs.Statement is ExpressionStatementSyntax es2)
        {
            TryDelegateInvocation(es2.Expression, ctx);
        }
        // else simple 'return' (if(cond) return;) -> only guard kept
    }

    private void HandleExpressionStatement(ExpressionStatementSyntax es, RoslynContext ctx) => TryDelegateInvocation(es.Expression, ctx);

    private bool TryDelegateInvocation(ExpressionSyntax expr, RoslynContext ctx)
    {
        if (expr is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
        {
            var exprText = ma.Expression.ToString();
            if (exprText.EndsWith("Facade", StringComparison.Ordinal) || exprText.EndsWith("Service", StringComparison.Ordinal) || exprText == "Router")
            {
                AddNode("delegate", exprText + "." + ma.Name.Identifier.ValueText, inv, ctx);
                return true;
            }
        }
        return false;
    }

    private void AddNode(string kind, string detail, SyntaxNode node, RoslynContext ctx)
    {
        var (file, line) = LocationUtil.GetLocation(node, ctx);
        _nodes.Add(new FlowNode(-1, kind, detail, file, line));
    }

    private void _logVerbose(string message)
    {
        if (_verbose) _log?.Invoke(message);
    }
}
