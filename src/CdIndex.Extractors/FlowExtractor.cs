using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class FlowExtractor : IExtractor
{
    private readonly string _handlerType;
    private readonly string _methodName;
    private readonly List<FlowNode> _nodes = new();

    public FlowExtractor(string handlerType, string methodName = "HandleAsync")
    {
        _handlerType = handlerType;
        _methodName = methodName;
    }

    public IReadOnlyList<FlowNode> Nodes => _nodes;

    public void Extract(RoslynContext context)
    {
        _nodes.Clear();
        foreach (var project in context.Solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = doc.GetSyntaxRootAsync().Result as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = doc.GetSemanticModelAsync().Result;
                if (semantic == null) continue;

                foreach (var decl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var name = decl.Identifier.ValueText;
                    if (name != _handlerType) continue; // simple name match
                    var method = decl.Members.OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault(m => m.Identifier.ValueText == _methodName);
                    if (method == null) continue;
                    ProcessMethod(method, context);
                }
            }
        }
    for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            _nodes[i] = n with { Order = i };
        }
    }

    private void ProcessMethod(MethodDeclarationSyntax method, RoslynContext ctx)
    {
        if (method.Body == null) return;
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
                default:
                    break;
            }
        }
    }

    private void HandleIf(IfStatementSyntax ifs, RoslynContext ctx)
    {
        // Pattern collapse: if (Cond()) { Facade.Handle(); return; } -> delegate only (skip guard)
        if (ifs.Statement is BlockSyntax blk && blk.Statements.Count == 2 &&
            blk.Statements[0] is ExpressionStatementSyntax firstExpr &&
            blk.Statements[1] is ReturnStatementSyntax)
        {
            if (TryDelegateInvocation(firstExpr.Expression, ctx, add: false))
            {
                // Add only delegate node (skip guard) per heuristic
                TryDelegateInvocation(firstExpr.Expression, ctx, add: true);
                return;
            }
        }

        // Standard guard (includes simple 'return;' statement or other forms)
        AddNode("guard", ifs.Condition.ToString(), ifs, ctx);

        if (ifs.Statement is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
            {
                if (inner is ExpressionStatementSyntax es)
                {
                    TryDelegateInvocation(es.Expression, ctx);
                }
                else if (inner is ReturnStatementSyntax rs)
                {
                    AddNode("return", "return", rs, ctx);
                }
            }
        }
        else if (ifs.Statement is ExpressionStatementSyntax es2)
        {
            TryDelegateInvocation(es2.Expression, ctx);
        }
    }

    private void HandleExpressionStatement(ExpressionStatementSyntax es, RoslynContext ctx)
    {
        TryDelegateInvocation(es.Expression, ctx);
    }

    private bool TryDelegateInvocation(ExpressionSyntax expr, RoslynContext ctx, bool add = true)
    {
        if (expr is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
        {
            var exprText = ma.Expression.ToString();
            if (exprText.EndsWith("Facade", StringComparison.Ordinal) || exprText.EndsWith("Service", StringComparison.Ordinal) || exprText == "Router")
            {
                if (add)
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
}
