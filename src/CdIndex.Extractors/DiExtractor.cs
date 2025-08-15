using System.Text;
using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class DiExtractor : IExtractor, IExtractor<DiRegistration>
{
    private readonly List<DiRegistration> _registrations = new();
    private readonly List<HostedService> _hostedServices = new();
    private readonly bool _dedupe;
    private readonly StringBuilder? _dbg;

    // Full qualification without global:: and with special types (int, string, etc.)
    private static readonly SymbolDisplayFormat FullNoGlobal = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public DiExtractor(bool diDedupe = false, StringBuilder? debugLog = null)
    {
        _dedupe = diDedupe;
        _dbg = debugLog;
    }

    public IReadOnlyList<DiRegistration> Registrations => _registrations;
    IReadOnlyList<DiRegistration> IExtractor<DiRegistration>.Items => _registrations;
    public IReadOnlyList<HostedService> HostedServices => _hostedServices;

    private static string GetTypeName(ITypeSymbol s) => s.ToDisplayString(FullNoGlobal);
    private static bool IsQualified(string name) => name.Contains('.');
    private void Log(string where, string iface, string impl, string why)
    {
        _dbg?.AppendLine($"[REG][{where}] {iface} -> {impl} :: {why}");
    }
    private void LogHosted(string where, string type, string why)
    {
        _dbg?.AppendLine($"[HOST][{where}] {type} :: {why}");
    }

    public void Extract(RoslynContext context)
    {
        _registrations.Clear();
        _hostedServices.Clear();
        // Extraction start (debug only when buffer provided)
        Log("INIT", "-", "-", "begin");
        foreach (var project in context.Solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath?.EndsWith(".cs") != true) continue;

                var root = document.GetSyntaxRootAsync().Result;
                if (root == null) continue;

                var semanticModel = document.GetSemanticModelAsync().Result;
                if (semanticModel == null) continue;

                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    ProcessInvocation(invocation, semanticModel, context);
                }
            }
        }

        // Optional dedupe (keep first occurrence by Interface+Implementation+Lifetime)
        if (_dedupe)
        {
            var seen = new HashSet<(string, string, string)>();
            var filtered = new List<DiRegistration>(_registrations.Count);
            foreach (var r in _registrations)
            {
                var key = (r.Interface, r.Implementation, r.Lifetime);
                if (seen.Add(key)) filtered.Add(r);
            }
            _registrations.Clear();
            _registrations.AddRange(filtered);
        }

        // Sort results deterministically
        _registrations.Sort((a, b) =>
        {
            var interfaceCompare = StringComparer.Ordinal.Compare(a.Interface, b.Interface);
            if (interfaceCompare != 0) return interfaceCompare;
            var implCompare = StringComparer.Ordinal.Compare(a.Implementation, b.Implementation);
            if (implCompare != 0) return implCompare;
            var fileCompare = StringComparer.Ordinal.Compare(a.File, b.File);
            if (fileCompare != 0) return fileCompare;
            return a.Line.CompareTo(b.Line);
        });

        _hostedServices.Sort((a, b) =>
        {
            var typeCompare = StringComparer.Ordinal.Compare(a.Type, b.Type);
            if (typeCompare != 0) return typeCompare;

            var fileCompare = StringComparer.Ordinal.Compare(a.File, b.File);
            if (fileCompare != 0) return fileCompare;

            return a.Line.CompareTo(b.Line);
        });
        Log("DONE", _registrations.Count.ToString(), _hostedServices.Count.ToString(), "sorted");
    }

    private void ProcessInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel, RoslynContext context)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null) return;

        var methodName = memberAccess.Name.Identifier.ValueText;

        // Check if this is a DI registration method
        if (!IsDiRegistrationMethod(methodName)) return;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol method && IsDependencyInjectionMethod(method))
        {
            var (file, line) = LocationUtil.GetLocation(invocation, context);
            if (methodName == "AddHostedService")
            {
                HandleHosted(method, file, line, "GEN");
            }
            else
            {
                HandleRegistration(invocation, method, methodName, semanticModel, file, line, "GEN");
            }
        }
        else
        {
            Fallback(invocation, semanticModel, methodName, context);
        }
    }

    private static bool IsDiRegistrationMethod(string methodName)
    {
        return methodName is "AddSingleton" or "AddScoped" or "AddTransient" or "AddHostedService";
    }

    private static string? GetEnclosingNamespace(SyntaxNode node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            switch (current)
            {
                case FileScopedNamespaceDeclarationSyntax fs:
                    return fs.Name.ToString();
                case NamespaceDeclarationSyntax ns:
                    return ns.Name.ToString();
            }
        }
        return null;
    }

    private static bool IsDependencyInjectionMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null) return false;

        // Check if it's from Microsoft.Extensions.DependencyInjection namespace
        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        return namespaceName?.StartsWith("Microsoft.Extensions.DependencyInjection") == true;
    }

    private void HandleHosted(IMethodSymbol method, string file, int line, string tag)
    {
        if (!method.IsGenericMethod || method.TypeArguments.Length != 1) return;
        var hosted = GetTypeName(method.TypeArguments[0]);
        LogHosted(tag, hosted, "raw");
        if (IsQualified(hosted)) { _hostedServices.Add(new HostedService(hosted, file, line)); LogHosted(tag, hosted, "ADDED"); }
        else LogHosted(tag, hosted, "SKIP not qualified");
    }

    private void HandleRegistration(InvocationExpressionSyntax invocation, IMethodSymbol method, string methodName, SemanticModel model, string file, int line, string tag)
    {
        if (!method.IsGenericMethod) return;
        var lifetime = GetLifetime(methodName);
        var args = method.TypeArguments;
        if (args.Length == 2)
        {
            var iface = GetTypeName(args[0]);
            var impl = GetTypeName(args[1]);
            Log(tag + "2", iface, impl, "raw");
            if (!IsExceptionSymbol(args[1]) && IsQualified(iface) && IsQualified(impl))
            {
                _registrations.Add(new DiRegistration(iface, impl, lifetime, file, line));
                Log(tag + "2", iface, impl, "ADDED");
            }
            else
            {
                Log(tag + "2", iface, impl, "SKIP filtered");
            }
        }
        else if (args.Length == 1)
        {
            var service = GetTypeName(args[0]);
            if (HasFactoryParameter(invocation))
            {
                var impl = TryExtractFactorySymbol(invocation, model) ?? TryExtractFactoryTypeName(invocation) ?? "(factory)";
                Log(tag + "1F", service, impl, "raw");
                if (impl != "(factory)" && !IsExceptionType(impl) && IsQualified(service) && IsQualified(impl))
                {
                    _registrations.Add(new DiRegistration(service, impl, lifetime, file, line));
                    Log(tag + "1F", service, impl, "ADDED");
                }
                else
                {
                    Log(tag + "1F", service, impl, "SKIP filtered");
                }
            }
            else
            {
                Log(tag + "1S", service, service, "raw");
                if (!IsExceptionType(service) && IsQualified(service))
                {
                    _registrations.Add(new DiRegistration(service, service, lifetime, file, line));
                    Log(tag + "1S", service, service, "ADDED");
                }
                else
                {
                    Log(tag + "1S", service, service, "SKIP filtered");
                }
            }
        }
    }

    private void Fallback(InvocationExpressionSyntax invocation, SemanticModel model, string methodName, RoslynContext ctx)
    {
        try
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax g }) return;
            var typeArgs = g.TypeArgumentList.Arguments;
            var (file, line) = LocationUtil.GetLocation(invocation, ctx);
            var lifetime = GetLifetime(methodName);
            if (methodName == "AddHostedService" && typeArgs.Count == 1)
            {
                var sym = model.GetTypeInfo(typeArgs[0]).Type;
                if (sym != null)
                {
                    var hosted = GetTypeName(sym);
                    LogHosted("FB", hosted, "raw");
                    if (IsQualified(hosted))
                    {
                        _hostedServices.Add(new HostedService(hosted, file, line));
                        LogHosted("FB", hosted, "ADDED");
                    }
                    else
                    {
                        LogHosted("FB", hosted, "SKIP not qualified");
                    }
                }
                return;
            }
            if (typeArgs.Count == 2)
            {
                var ifaceSym = model.GetTypeInfo(typeArgs[0]).Type;
                var implSym = model.GetTypeInfo(typeArgs[1]).Type;
                if (ifaceSym != null && implSym != null)
                {
                    var iface = GetTypeName(ifaceSym);
                    var impl = GetTypeName(implSym);
                    Log("FB2", iface, impl, "raw");
                    if (!IsExceptionType(impl) && IsQualified(iface) && IsQualified(impl))
                    {
                        _registrations.Add(new DiRegistration(iface, impl, lifetime, file, line));
                        Log("FB2", iface, impl, "ADDED");
                    }
                    else
                    {
                        Log("FB2", iface, impl, "SKIP filtered");
                    }
                }
            }
            else if (typeArgs.Count == 1)
            {
                var svcSym = model.GetTypeInfo(typeArgs[0]).Type;
                if (svcSym != null)
                {
                    var service = GetTypeName(svcSym);
                    if (HasFactoryParameter(invocation))
                    {
                        var impl = TryExtractFactorySymbol(invocation, model) ?? TryExtractFactoryTypeName(invocation) ?? "(factory)";
                        Log("FB1F", service, impl, "raw");
                        if (impl != "(factory)" && !IsExceptionType(impl) && IsQualified(service) && IsQualified(impl))
                        {
                            _registrations.Add(new DiRegistration(service, impl, lifetime, file, line));
                            Log("FB1F", service, impl, "ADDED");
                        }
                        else
                        {
                            Log("FB1F", service, impl, "SKIP filtered");
                        }
                    }
                    else
                    {
                        Log("FB1S", service, service, "raw");
                        if (!IsExceptionType(service) && IsQualified(service))
                        {
                            _registrations.Add(new DiRegistration(service, service, lifetime, file, line));
                            Log("FB1S", service, service, "ADDED");
                        }
                        else
                        {
                            Log("FB1S", service, service, "SKIP filtered");
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static bool HasFactoryParameter(InvocationExpressionSyntax invocation) => invocation.ArgumentList.Arguments.Count > 0;
    private static string? TryExtractFactoryTypeName(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0) return null;
        var expr = invocation.ArgumentList.Arguments[0].Expression;
        SyntaxNode? body = expr switch
        {
            SimpleLambdaExpressionSyntax s => s.Body,
            ParenthesizedLambdaExpressionSyntax p => p.Body,
            _ => null
        };
        if (body == null) return null;
        if (body is ObjectCreationExpressionSyntax directCreation)
        {
            return directCreation.Type.ToString();
        }
        var creation = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        return creation?.Type.ToString();
    }
    private string? TryExtractFactorySymbol(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.ArgumentList.Arguments.Count == 0) return null;
        var expr = invocation.ArgumentList.Arguments[0].Expression;
        SyntaxNode? body = expr switch
        {
            SimpleLambdaExpressionSyntax s => s.Body,
            ParenthesizedLambdaExpressionSyntax p => p.Body,
            _ => null
        };
        if (body == null) return null;
        ObjectCreationExpressionSyntax? creation = null;
        if (body is ObjectCreationExpressionSyntax directCreation)
        {
            creation = directCreation;
        }
        else
        {
            creation = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        }
        if (creation == null) return null;
        var sym = model.GetSymbolInfo(creation.Type).Symbol as ITypeSymbol ?? model.GetTypeInfo(creation.Type).Type;
        return sym == null ? null : GetTypeName(sym);
    }

    private static string GetLifetime(string methodName) => methodName switch
    {
        "AddSingleton" => "Singleton",
        "AddScoped" => "Scoped",
        "AddTransient" => "Transient",
        _ => "Transient"
    };
    private static bool IsExceptionType(string typeName) => typeName.EndsWith("Exception", StringComparison.Ordinal);
    private static bool IsExceptionSymbol(ITypeSymbol symbol)
    {
        var current = symbol;
        while (current != null)
        {
            if (current.Name == "Exception" && current.ContainingNamespace?.ToDisplayString() == "System") return true;
            current = current.BaseType;
        }
        return false;
    }
}
