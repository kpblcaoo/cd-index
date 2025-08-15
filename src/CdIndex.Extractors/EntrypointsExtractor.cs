using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax; // Needed for ClassDeclarationSyntax, MethodDeclarationSyntax, etc.

namespace CdIndex.Extractors;

public sealed class EntrypointsExtractor : IExtractor, IExtractor<EntrypointsSection>
{
    // Reuse deterministic full qualification similar to DI extractor (without global::, include namespaces & containing types)
    private static readonly SymbolDisplayFormat FullNoGlobal = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
    private readonly List<EntrypointsSection> _sections = new();
    private readonly HashSet<(string Type, string File, int Line)> _seedHosted = new();
    public IReadOnlyList<EntrypointsSection> Sections => _sections;
    IReadOnlyList<EntrypointsSection> IExtractor<EntrypointsSection>.Items => _sections;

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
                var comp = RoslynSync.GetCompilation(project);
                var entry = comp?.GetEntryPoint(System.Threading.CancellationToken.None);
                if (entry != null && entry.DeclaringSyntaxReferences.Length > 0)
                {
                    var syntaxRef = entry.DeclaringSyntaxReferences[0];
                    var node = syntaxRef.GetSyntax() as CSharpSyntaxNode;
                    // Document retrieval not strictly needed for path; kept minimal
                    var filePath = syntaxRef.SyntaxTree?.FilePath;
                    if (filePath != null)
                    {
                        var relFile = PathEx.Normalize(filePath, context.RepoRoot);
                        var (file, line) = LocationUtil.GetLocation(node ?? syntaxRef.GetSyntax(), context);
                        // Type name might be synthesized (<Program>$) so only include if not compiler generated pattern
                        var typeName = entry.ContainingType?.ToDisplayString(FullNoGlobal);
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
                var root = RoslynSync.GetRoot(document) as CSharpSyntaxNode;
                if (root == null) continue;
                var semantic = RoslynSync.GetModel(document);
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

            var relProjectFile = PathEx.Normalize(project.FilePath ?? string.Empty, context.RepoRoot);

            _sections.Add(new EntrypointsSection(
                new ProjectRef(project.Name, relProjectFile),
                programMain,
                hostedDistinct
            ));
        }

        // Deterministic ordering of sections (Project.Name then Project.File)
        var ordered = _sections
            .OrderBy(s => s.Project.Name, System.StringComparer.Ordinal)
            .ThenBy(s => s.Project.File, System.StringComparer.Ordinal)
            .ToList();
        _sections.Clear();
        _sections.AddRange(ordered);
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
                        var typeName = symbol?.ToDisplayString(FullNoGlobal);
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
            string? typeName = null;
            if (symbol != null && symbol.IsGenericMethod && symbol.TypeArguments.Length == 1)
            {
                typeName = symbol.TypeArguments[0].ToDisplayString(FullNoGlobal);
            }
            else if (invocation.Expression is MemberAccessExpressionSyntax mae && invocation.DescendantNodes().OfType<GenericNameSyntax>().FirstOrDefault() is GenericNameSyntax g && g.TypeArgumentList.Arguments.Count == 1)
            {
                // Fallback: syntactic generic AddHostedService<MyType>() when semantic failed
                // Attempt to get semantic symbol for fallback for full qualification
                var typeSyntax = g.TypeArgumentList.Arguments[0];
                var model = semantic.Compilation.GetSemanticModel(typeSyntax.SyntaxTree);
                var sym = model.GetTypeInfo(typeSyntax).Type;
                typeName = sym?.ToDisplayString(FullNoGlobal) ?? typeSyntax.ToString();
            }
            if (typeName == null) continue;
            var (file, line) = LocationUtil.GetLocation(invocation, ctx);
            target.Add(new HostedService(typeName, file, line));
        }
    }
}
