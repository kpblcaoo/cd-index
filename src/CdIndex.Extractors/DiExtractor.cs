using System.Text;
using CdIndex.Core;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CdIndex.Extractors;

public sealed class DiExtractor : IExtractor
{
    private readonly List<DiRegistration> _registrations = new();
    private readonly List<HostedService> _hostedServices = new();
    private readonly bool _dedupe;

    public DiExtractor(bool diDedupe = false)
    {
        _dedupe = diDedupe;
    }

    public IReadOnlyList<DiRegistration> Registrations => _registrations;
    public IReadOnlyList<HostedService> HostedServices => _hostedServices;

    public void Extract(RoslynContext context)
    {
        _registrations.Clear();
        _hostedServices.Clear();

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
            // Normal (fully bound) path
            var (file, line) = LocationUtil.GetLocation(invocation, context);
            if (methodName == "AddHostedService")
            {
                ProcessHostedService(invocation, method, file, line);
            }
            else
            {
                ProcessServiceRegistration(invocation, method, methodName, file, line);
            }
            return;
        }

        // Fallback heuristic: symbol binding failed (e.g. NuGet restore not available in some CI context)
        // but we still want to capture obvious DI registrations to keep cross-platform determinism.
        // We rely purely on method name + generic arity + presence of factory lambda.
        try
        {
            var (file, line) = LocationUtil.GetLocation(invocation, context);
            if (invocation.Expression is MemberAccessExpressionSyntax ma && ma.Name is GenericNameSyntax gName)
            {
                var typeArgs = gName.TypeArgumentList.Arguments;
                if (methodName == "AddHostedService" && typeArgs.Count == 1)
                {
                    var hostedType = typeArgs[0].ToString();
                    _hostedServices.Add(new HostedService(hostedType, file, line));
                    return;
                }

                var lifetime = GetLifetime(methodName);
                if (typeArgs.Count == 2)
                {
                    if (!IsExceptionType(typeArgs[1].ToString()))
                        _registrations.Add(new DiRegistration(typeArgs[0].ToString(), typeArgs[1].ToString(), lifetime, file, line));
                }
                else if (typeArgs.Count == 1)
                {
                    // Factory or self-binding
                    var impl = HasFactoryParameter(invocation) ? (TryExtractFactoryType(invocation) ?? "(factory)") : typeArgs[0].ToString();
                    if (!IsExceptionType(impl))
                        _registrations.Add(new DiRegistration(typeArgs[0].ToString(), impl, lifetime, file, line));
                }
            }
        }
        catch
        {
            // Swallow – heuristic fallback should never make test fail catastrophically; absence will be caught by tests.
        }
    }

    private static bool IsDiRegistrationMethod(string methodName)
    {
        return methodName is "AddSingleton" or "AddScoped" or "AddTransient" or "AddHostedService";
    }

    private static bool IsDependencyInjectionMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null) return false;

        // Check if it's from Microsoft.Extensions.DependencyInjection namespace
        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        return namespaceName?.StartsWith("Microsoft.Extensions.DependencyInjection") == true;
    }

    private void ProcessHostedService(InvocationExpressionSyntax invocation, IMethodSymbol method, string file, int line)
    {
        if (!method.IsGenericMethod || method.TypeArguments.Length != 1) return;

    var hostedType = method.TypeArguments[0];
    var typeName = hostedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        _hostedServices.Add(new HostedService(typeName, file, line));
    }

    private void ProcessServiceRegistration(InvocationExpressionSyntax invocation, IMethodSymbol method, string methodName, string file, int line)
    {
        var lifetime = GetLifetime(methodName);
        
        if (method.IsGenericMethod)
        {
            ProcessGenericRegistration(method, lifetime, file, line, invocation);
        }
        else
        {
            // Non-generic overloads - not implemented for P0
        }
    }

    private void ProcessGenericRegistration(IMethodSymbol method, string lifetime, string file, int line, InvocationExpressionSyntax invocation)
    {
        var typeArgs = method.TypeArguments;
        
        if (typeArgs.Length == 2)
        {
            // AddSingleton<TInterface, TImplementation>()
            var interfaceType = typeArgs[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var implementationType = typeArgs[1].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            
            if (!IsExceptionSymbol(method.TypeArguments[1]))
                _registrations.Add(new DiRegistration(interfaceType, implementationType, lifetime, file, line));
        }
        else if (typeArgs.Length == 1)
        {
            var serviceType = typeArgs[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            
            // Check if this is a factory registration
            if (HasFactoryParameter(invocation))
            {
                // AddSingleton<TInterface>(sp => new TImpl(...))
                var implementationType = TryExtractFactoryType(invocation) ?? "(factory)";
                if (!IsExceptionType(implementationType))
                    _registrations.Add(new DiRegistration(serviceType, implementationType, lifetime, file, line));
            }
            else
            {
                // AddSingleton<TImplementation>() - self-binding
                if (!IsExceptionType(serviceType))
                    _registrations.Add(new DiRegistration(serviceType, serviceType, lifetime, file, line));
            }
        }
    }

    private static bool HasFactoryParameter(InvocationExpressionSyntax invocation)
    {
        return invocation.ArgumentList.Arguments.Count > 0;
    }

    private static string? TryExtractFactoryType(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0) return null;

        var firstArg = invocation.ArgumentList.Arguments[0];
        
        // Look for lambda expressions: sp => new SomeType(...)
        if (firstArg.Expression is SimpleLambdaExpressionSyntax lambda)
        {
            return TryExtractTypeFromLambda(lambda);
        }
        
        if (firstArg.Expression is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            return TryExtractTypeFromLambda(parenLambda.Body);
        }

        return null;
    }

    private static string? TryExtractTypeFromLambda(SyntaxNode lambdaBody)
    {
        // Look for object creation expressions: new SomeType(...)
        var objectCreations = lambdaBody.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
        var firstCreation = objectCreations.FirstOrDefault();
        
        if (firstCreation?.Type is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText;
        }
        
        if (firstCreation?.Type is QualifiedNameSyntax qualified)
        {
            return qualified.ToString();
        }

        return null;
    }

    private static string GetLifetime(string methodName)
    {
        return methodName switch
        {
            "AddSingleton" => "Singleton",
            "AddScoped" => "Scoped",
            "AddTransient" => "Transient",
            _ => "Transient"
        };
    }

    // Removed GetTypeName indirection; using ToDisplayString directly.

    private static bool IsExceptionType(string typeName)
    {
        // Simple heuristic: ends with Exception OR contains ".Exception" or global qualified forms.
        return typeName.EndsWith("Exception", StringComparison.Ordinal);
    }

    private static bool IsExceptionSymbol(ITypeSymbol symbol)
    {
        // Walk base types
        var current = symbol;
        while (current != null)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Exception") return true;
            if (current.Name == "Exception" && current.ContainingNamespace?.ToDisplayString() == "System") return true;
            current = current.BaseType;
        }
        return false;
    }
}
