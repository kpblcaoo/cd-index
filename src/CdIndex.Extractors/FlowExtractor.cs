using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class FlowExtractor : IExtractor, IExtractor<FlowNode>
{
    private readonly string _handlerTypeInput;
    private readonly string _methodName;
    private readonly List<FlowNode> _nodes = new();
    private readonly bool _verbose;
    private readonly Action<string>? _log;
    private readonly string[] _delegateSuffixes;

    public FlowExtractor(string handlerType,
        string methodName = "HandleAsync",
        bool verbose = false,
        Action<string>? log = null,
        IEnumerable<string>? delegateSuffixes = null)
    {
        _handlerTypeInput = handlerType;
        _methodName = methodName;
        _verbose = verbose;
        _log = log;
        _delegateSuffixes = (delegateSuffixes == null || !delegateSuffixes.Any())
            ? new[] { "Router", "Facade", "Service", "Dispatcher", "Processor", "Manager", "Module" }
            : delegateSuffixes.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<FlowNode> Nodes => _nodes;
    IReadOnlyList<FlowNode> IExtractor<FlowNode>.Items => _nodes;

    public void Extract(RoslynContext context)
    {
        _nodes.Clear();

        // Collect candidate class symbols (simple pass over syntax for performance determinism)
        var candidates = new List<(INamedTypeSymbol Symbol, ClassDeclarationSyntax Decl, SemanticModel Model)>();
        var lastIdentifier = _handlerTypeInput.Contains('.') ? _handlerTypeInput.Split('.').Last() : _handlerTypeInput;

        foreach (var project in context.Solution.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            Compilation? comp = null;
            try { comp = RoslynSync.GetCompilation(project); } catch { }
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = RoslynSync.GetRoot(doc) as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = RoslynSync.GetModel(doc);
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
                var comp = RoslynSync.GetCompilation(project);
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
                            var doc = project.Documents.FirstOrDefault(d => RoslynSync.GetRoot(d) == declNode.SyntaxTree.GetRoot());
                            var model = doc == null ? null : RoslynSync.GetModel(doc);
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

        // Acquire syntax of chosen method (prefer declaration that has a body or expression body)
        MethodDeclarationSyntax? methodSyntax = null;
        foreach (var declRef in methodSymbol.DeclaringSyntaxReferences)
        {
            if (declRef.GetSyntax() is MethodDeclarationSyntax m)
            {
                if (m.Body != null || m.ExpressionBody != null)
                {
                    methodSyntax = m; break;
                }
                methodSyntax ??= m; // keep first as fallback
            }
        }
        if (methodSyntax == null)
            throw new InvalidOperationException("flow method syntax not found (metadata-only)");

        if (methodSyntax.Body == null && methodSyntax.ExpressionBody != null)
        {
            // Simple expression-bodied method: attempt delegate detection on expression
            var expr = methodSyntax.ExpressionBody.Expression;
            TryDelegateInvocation(expr, context, chosenModel);
        }
        else
        {
            ProcessMethod(methodSyntax, context, chosenModel);
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            _nodes[i] = n with { Order = i };
        }

        if (_nodes.Count == 0)
            _logVerbose("[flow] 0 nodes; nothing matched top-level patterns");
        else
        {
            var guards = _nodes.Count(n => n.Kind == "guard");
            var delegates = _nodes.Count(n => n.Kind == "delegate");
            var returns = _nodes.Count(n => n.Kind == "return");
            _logVerbose($"[flow] nodes: {_nodes.Count} (guards={guards} delegates={delegates} returns={returns})");
        }
    }

    private static bool IsAllowedReturn(ITypeSymbol t)
    {
        if (t == null) return false;
        var name = t.Name;
        if (name == "Void") return true;
        if (name == "Task" || name == "ValueTask") return true;
        return false;
    }

    private void ProcessMethod(MethodDeclarationSyntax method, RoslynContext ctx, SemanticModel model)
    {
        if (method.Body == null) return; // expression-bodied handled earlier
        foreach (var stmt in method.Body.Statements)
            ProcessTopLevelStatement(stmt, ctx, model, depth: 0);
    }

    // Process a statement as if it were top-level; flatten certain wrappers (try, block, using) one level deep only.
    private void ProcessTopLevelStatement(StatementSyntax stmt, RoslynContext ctx, SemanticModel model, int depth)
    {
        // Limit depth to avoid uncontrolled descent (depth 0 = method level, we allow unwrapping wrappers only once)
        switch (stmt)
        {
            case IfStatementSyntax ifs:
                HandleIf(ifs, ctx, model);
                break;
            case SwitchStatementSyntax sw:
                HandleSwitch(sw, ctx, model);
                break;
            case ExpressionStatementSyntax es:
                TryDelegateInvocation(es.Expression, ctx, model);
                break;
            case LocalDeclarationStatementSyntax lds:
                foreach (var v in lds.Declaration.Variables)
                    if (v.Initializer != null)
                        TryDelegateInvocation(v.Initializer.Value, ctx, model);
                break;
            case ReturnStatementSyntax rs:
                AddNode("return", "return", rs, ctx);
                break;
            case TryStatementSyntax ts when depth == 0:
                // Flatten primary try block only
                foreach (var inner in ts.Block.Statements)
                    ProcessTopLevelStatement(inner, ctx, model, depth + 1);
                break;
            case BlockSyntax block when depth == 0:
                foreach (var inner in block.Statements)
                    ProcessTopLevelStatement(inner, ctx, model, depth + 1);
                break;
            case UsingStatementSyntax us when depth == 0:
                if (us.Statement is BlockSyntax ub)
                {
                    foreach (var inner in ub.Statements)
                        ProcessTopLevelStatement(inner, ctx, model, depth + 1);
                }
                else if (us.Statement is StatementSyntax single)
                {
                    ProcessTopLevelStatement(single, ctx, model, depth + 1);
                }
                break;
            // Loops: treat body as potential container for a single delegate at start (common command routing pattern)
            case ForEachStatementSyntax fe when depth == 0:
                if (fe.Statement is BlockSyntax feb)
                    foreach (var inner in feb.Statements.Take(3)) // small cap for determinism; only top few
                        if (inner is ExpressionStatementSyntax ies)
                            TryDelegateInvocation(ies.Expression, ctx, model);
                break;
            case ForStatementSyntax fs when depth == 0:
                if (fs.Statement is BlockSyntax fsb)
                    foreach (var inner in fsb.Statements.Take(3))
                        if (inner is ExpressionStatementSyntax ies)
                            TryDelegateInvocation(ies.Expression, ctx, model);
                break;
            case WhileStatementSyntax ws when depth == 0:
                if (ws.Statement is BlockSyntax wsb)
                    foreach (var inner in wsb.Statements.Take(3))
                        if (inner is ExpressionStatementSyntax ies)
                            TryDelegateInvocation(ies.Expression, ctx, model);
                break;
        }
    }

    private void HandleIf(IfStatementSyntax ifs, RoslynContext ctx, SemanticModel model)
    {
        // Pattern A: if(cond) return [expr?]; => guard only
        if (ifs.Statement is ReturnStatementSyntax)
        {
            AddNode("guard", ifs.Condition.ToString(), ifs, ctx);
            return;
        }

        // Pattern B: collapsed delegate: if(cond){ Delegate(); return; }
        if (ifs.Statement is BlockSyntax blk && blk.Statements.Count == 2 &&
            blk.Statements[0] is ExpressionStatementSyntax firstExpr &&
            blk.Statements[1] is ReturnStatementSyntax &&
            TryDelegateInvocation(firstExpr.Expression, ctx, model))
        {
            return; // delegate only (collapsed)
        }

        // Pattern C: guard + (optional first delegate in block)
        AddNode("guard", ifs.Condition.ToString(), ifs, ctx);
        if (ifs.Statement is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
            {
                if (inner is ExpressionStatementSyntax es)
                {
                    if (TryDelegateInvocation(es.Expression, ctx, model)) break; // only first delegate inside guard
                }
            }
        }
    }

    private void HandleSwitch(SwitchStatementSyntax sw, RoslynContext ctx, SemanticModel model)
    {
        // For each section pick first qualifying delegate invocation
        foreach (var section in sw.Sections)
        {
            foreach (var stmt in section.Statements)
            {
                if (stmt is ExpressionStatementSyntax es)
                {
                    if (TryDelegateInvocation(es.Expression, ctx, model)) break;
                }
            }
        }
    }

    private bool TryDelegateInvocation(ExpressionSyntax expr, RoslynContext ctx, SemanticModel model)
    {
        // Unwrap await
        if (expr is AwaitExpressionSyntax awaitExpr)
        {
            expr = awaitExpr.Expression;
        }
        if (expr is InvocationExpressionSyntax inv)
        {
            var symbol = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
            if (symbol?.ContainingType != null)
            {
                var typeName = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (_delegateSuffixes.Any(s => typeName.EndsWith(s, StringComparison.Ordinal)))
                {
                    var detail = typeName + "." + symbol.Name;
                    AddNode("delegate", detail, inv, ctx);
                    return true;
                }
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
