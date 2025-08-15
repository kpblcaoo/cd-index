using System.Text.RegularExpressions;
using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

/// <summary>
/// Extracts System.CommandLine command model: Command names, aliases, options, arguments.
/// Phase 1 scope: literal command names (string), aliases via AddAlias, options & arguments via:
///  - Inline object creation inside Command initializer
///  - Separate variables added through AddOption/AddArgument
///  - Direct inline new Option/Argument expressions passed to AddOption/AddArgument
/// Exclusions: dynamic names, generic factories, nested command hierarchy beyond direct creations.
/// </summary>
public sealed class CliCommandsExtractor : IExtractor, IExtractor<CliCommand>
{
    private readonly string? _allowRegex;
    private readonly List<CliCommand> _items = new();
    public IReadOnlyList<CliCommand> Items => _items;
    IReadOnlyList<CliCommand> IExtractor<CliCommand>.Items => _items;

    public CliCommandsExtractor(string? allowRegex = null)
    {
        _allowRegex = allowRegex;
    }

    public void Extract(RoslynContext context)
    {
        _items.Clear();
        var regex = SafeRegex(_allowRegex);
        foreach (var project in context.Solution.Projects)
        {
            var perProject = new List<CliCommand>();
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) != true) continue;
                var root = doc.GetSyntaxRootAsync().Result;
                var model = doc.GetSemanticModelAsync().Result;
                if (root == null || model == null) continue;
                var semantic = model; // non-null alias

                var commandCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                    .Where(o => o.Type.ToString().EndsWith("Command", StringComparison.Ordinal) && o.Type.ToString() != "RootCommand")
                    .ToList();

                var optionVarToToken = new Dictionary<string, string>(StringComparer.Ordinal);
                var argVarToName = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var vDecl in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
                {
                    var typeName = vDecl.Type.ToString();
                    bool isOption = typeName.StartsWith("Option", StringComparison.Ordinal);
                    bool isArgument = !isOption && typeName.StartsWith("Argument", StringComparison.Ordinal);
                    if (!isOption && !isArgument) continue;
                    foreach (var v in vDecl.Variables)
                    {
                        if (v.Initializer?.Value is ObjectCreationExpressionSyntax obj)
                        {
                            if (obj.ArgumentList?.Arguments.Count > 0)
                            {
                                var firstExpr = obj.ArgumentList.Arguments[0].Expression;
                                var cv = semantic.GetConstantValue(firstExpr);
                                if (cv.HasValue && cv.Value is string s && !string.IsNullOrWhiteSpace(s))
                                {
                                    if (isOption && s.StartsWith("--", StringComparison.Ordinal)) optionVarToToken[v.Identifier.Text] = s;
                                    else if (isArgument) argVarToName[v.Identifier.Text] = s;
                                }
                            }
                        }
                    }
                }

                foreach (var cmdObj in commandCreations)
                {
                    var argList = cmdObj.ArgumentList;
                    if (argList == null || argList.Arguments.Count == 0) continue;
                    var arg0 = argList.Arguments[0].Expression;
                    var cv = semantic.GetConstantValue(arg0); // model non-null due to earlier guard
                    if (!cv.HasValue || cv.Value is not string name || string.IsNullOrWhiteSpace(name)) continue;
                    if (regex != null && !regex.IsMatch(name)) continue;
                    var (relPath, line) = LocationUtil.GetLocation(cmdObj, context);
                    var file = relPath;
                    var aliases = new HashSet<string>(StringComparer.Ordinal);
                    var options = new HashSet<string>(StringComparer.Ordinal);
                    var arguments = new HashSet<string>(StringComparer.Ordinal);

                    if (cmdObj.Initializer != null)
                    {
                        foreach (var expr in cmdObj.Initializer.Expressions)
                        {
                            if (expr is ObjectCreationExpressionSyntax child)
                            {
                                var childType = child.Type.ToString();
                                if (childType.StartsWith("Option", StringComparison.Ordinal))
                                {
                                    var token = ExtractFirstStringArg(child, semantic);
                                    if (token != null && token.StartsWith("--", StringComparison.Ordinal)) options.Add(token);
                                }
                                else if (childType.StartsWith("Argument", StringComparison.Ordinal))
                                {
                                    var argName = ExtractFirstStringArg(child, semantic);
                                    if (!string.IsNullOrWhiteSpace(argName)) arguments.Add(argName!);
                                }
                            }
                        }
                    }

                    string? cmdVar = null;
                    if (cmdObj.Parent is EqualsValueClauseSyntax eq && eq.Parent is VariableDeclaratorSyntax vDecl2)
                        cmdVar = vDecl2.Identifier.Text;

                    if (cmdVar != null)
                    {
                        var parentStatement = cmdObj.FirstAncestorOrSelf<StatementSyntax>();
                        var block = parentStatement?.Parent as BlockSyntax;
                        if (block != null)
                        {
                            foreach (var stmt in block.Statements)
                            {
                                if (stmt.SpanStart < parentStatement!.SpanStart) continue;
                                foreach (var inv in stmt.DescendantNodes().OfType<InvocationExpressionSyntax>())
                                {
                                    if (inv.Expression is MemberAccessExpressionSyntax ma && ma.Expression.ToString() == cmdVar)
                                    {
                                        var method = ma.Name.Identifier.Text;
                                        if (method == "AddAlias" && inv.ArgumentList?.Arguments.Count > 0)
                                        {
                                            var aStr = ExtractFirstStringArg(inv, semantic);
                                            if (!string.IsNullOrWhiteSpace(aStr)) aliases.Add(aStr!);
                                        }
                                        else if ((method == "AddOption" || method == "AddArgument") && inv.ArgumentList?.Arguments.Count > 0)
                                        {
                                            var firstParam = inv.ArgumentList.Arguments[0].Expression;
                                            if (firstParam is ObjectCreationExpressionSyntax inlineCreated)
                                            {
                                                var tok = ExtractFirstStringArg(inlineCreated, semantic);
                                                if (method == "AddOption" && tok != null && tok.StartsWith("--", StringComparison.Ordinal)) options.Add(tok);
                                                else if (method == "AddArgument" && tok != null) arguments.Add(tok);
                                            }
                                            else if (firstParam is IdentifierNameSyntax id)
                                            {
                                                if (method == "AddOption" && optionVarToToken.TryGetValue(id.Identifier.Text, out var optTok)) options.Add(optTok);
                                                else if (method == "AddArgument" && argVarToName.TryGetValue(id.Identifier.Text, out var argTok)) arguments.Add(argTok);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    _items.Add(new CliCommand(name,
                        aliases.OrderBy(a => a, StringComparer.Ordinal).ToList(),
                        options.OrderBy(o => o, StringComparer.Ordinal).ToList(),
                        arguments.OrderBy(a => a, StringComparer.Ordinal).ToList(),
                        file, line));
                }
            }
        }

        _items.Sort((a, b) =>
        {
            var n = StringComparer.Ordinal.Compare(a.Name, b.Name);
            if (n != 0) return n;
            var f = StringComparer.Ordinal.Compare(a.File, b.File);
            if (f != 0) return f;
            return a.Line.CompareTo(b.Line);
        });
    }

    private static string? ExtractFirstStringArg(InvocationExpressionSyntax inv, SemanticModel model)
    {
        if (inv.ArgumentList?.Arguments.Count > 0)
        {
            var expr = inv.ArgumentList.Arguments[0].Expression;
            var cv = model.GetConstantValue(expr);
            if (cv.HasValue && cv.Value is string s) return s;
        }
        return null;
    }
    private static string? ExtractFirstStringArg(ObjectCreationExpressionSyntax obj, SemanticModel model)
    {
        if (obj.ArgumentList?.Arguments.Count > 0)
        {
            var expr = obj.ArgumentList.Arguments[0].Expression;
            var cv = model.GetConstantValue(expr);
            if (cv.HasValue && cv.Value is string s) return s;
        }
        return null;
    }
    private static Regex? SafeRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try { return new Regex(pattern, RegexOptions.Compiled); } catch { return null; }
    }
}
