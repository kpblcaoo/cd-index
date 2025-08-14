using System.Collections.Generic;
using System.Linq;
using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class EntrypointsExtractor : IExtractor
{
    private readonly List<EntrypointsSection> _sections = new();
    private readonly HashSet<(string Type, string File, int Line)> _seedHosted = new();
    public IReadOnlyList<EntrypointsSection> Sections => _sections;

    // Allow re-use of DI.HostedServices (issue #13 requirement)
    public void SeedHostedServices(IEnumerable<HostedService> hosted)
    {
        _seedHosted.Clear();
        foreach (var h in hosted)
            _seedHosted.Add((h.Type, h.File, h.Line));
    }

    public void Extract(RoslynContext context)
    {
        _sections.Clear();

        var projects = context.Solution.Projects
            .OrderBy(p => p.Name, System.StringComparer.Ordinal);

        foreach (var project in projects)
        {
            ProgramMain? programMain = null;
            var hosted = _seedHosted.Select(h => new HostedService(h.Type, h.File, h.Line)).ToList();

            // Detect top-level statements entry point first
            try
            {
                var comp = project.GetCompilationAsync().Result;
                var entry = comp?.GetEntryPoint(System.Threading.CancellationToken.None);
                if (entry != null && entry.DeclaringSyntaxReferences.Length > 0)
                {
                    var syntaxRef = entry.DeclaringSyntaxReferences[0];
                    var node = syntaxRef.GetSyntax() as CSharpSyntaxNode;
                    var doc = project.Documents.FirstOrDefault(d => d.GetSyntaxRootAsync().Result == node?.SyntaxTree.GetRoot());
                    // Fallback: map by path
                    var filePath = syntaxRef.SyntaxTree?.FilePath;
                    if (filePath != null)
                    {
                        var normalized = filePath.Replace("\\", "/");
                        var repoRootNorm2 = context.RepoRoot.Replace("\\", "/");
                        var relFile = normalized.StartsWith(repoRootNorm2, System.StringComparison.OrdinalIgnoreCase)
                            ? normalized.Substring(repoRootNorm2.Length).TrimStart('/')
                            : normalized;
                        var (file, line) = LocationUtil.GetLocation(node ?? syntaxRef.GetSyntax(), context);
                        // Type name might be synthesized (<Program>$) so only include if not compiler generated pattern
                        var typeName = entry.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                        programMain = new ProgramMain(file, line, typeName);
                    }
                }
            }
            catch { /* ignore and continue */ }

            var documents = project.Documents
                .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs"))
                .OrderBy(d => d.FilePath, System.StringComparer.Ordinal)
                .ToList();

            foreach (var document in documents)
            {
                var root = document.GetSyntaxRootAsync().Result as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = document.GetSemanticModelAsync().Result;
                if (semantic == null) continue;

                if (programMain == null)
                {
                    programMain = TryFindProgramMain(root, semantic, context);
                }

                CollectHostedServices(root, semantic, context, hosted);
            }

            // De-dup hosted (Type,File,Line)
            var hostedDistinct = hosted
                .GroupBy(h => (h.Type, h.File, h.Line))
                .Select(g => g.First())
                .OrderBy(h => h.Type, System.StringComparer.Ordinal)
                .ThenBy(h => h.File, System.StringComparer.Ordinal)
                .ThenBy(h => h.Line)
                .ToList();

            var projectFile = (project.FilePath ?? string.Empty).Replace("\\", "/");
            var repoRootNorm = context.RepoRoot.Replace("\\", "/");
            var relProjectFile = projectFile.StartsWith(repoRootNorm, StringComparison.OrdinalIgnoreCase)
                ? projectFile.Substring(repoRootNorm.Length).TrimStart('/')
                : projectFile;

            _sections.Add(new EntrypointsSection(
                new ProjectRef(project.Name, relProjectFile),
                programMain,
                hostedDistinct
            ));
        }
    }

    private static ProgramMain? TryFindProgramMain(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx)
    {
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var cls in classes)
        {
            if (cls.Identifier.ValueText != "Program") continue;
            var methods = cls.Members.OfType<MethodDeclarationSyntax>();
            foreach (var m in methods)
            {
                if (m.Identifier.ValueText != "Main") continue;
                if (!m.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
                // Return type void or int
                if (m.ReturnType is PredefinedTypeSyntax pts &&
                    (pts.Keyword.IsKind(SyntaxKind.VoidKeyword) || pts.Keyword.IsKind(SyntaxKind.IntKeyword)))
                {
                    // 0 or 1 parameter string[]
                    if (m.ParameterList.Parameters.Count <= 1)
                    {
                        if (m.ParameterList.Parameters.Count == 1)
                        {
                            var p = m.ParameterList.Parameters[0];
                            if (p.Type is not ArrayTypeSyntax arr ||
                                arr.ElementType is not PredefinedTypeSyntax el ||
                                !el.Keyword.IsKind(SyntaxKind.StringKeyword))
                                continue;
                        }
                        var (file, line) = LocationUtil.GetLocation(m, ctx);
                        var symbol = semantic.GetDeclaredSymbol(cls);
                        var typeName = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                        return new ProgramMain(file, line, typeName);
                    }
                }
            }
        }
        return null;
    }

    private static void CollectHostedServices(CSharpSyntaxNode root, SemanticModel semantic, RoslynContext ctx, List<HostedService> target)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax ma) continue;
            if (ma.Name.Identifier.ValueText != "AddHostedService") continue;
            var symbol = semantic.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol == null || !symbol.IsGenericMethod || symbol.TypeArguments.Length != 1) continue;
            var (file, line) = LocationUtil.GetLocation(invocation, ctx);
            var typeName = symbol.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            target.Add(new HostedService(typeName, file, line));
        }
    }
}
