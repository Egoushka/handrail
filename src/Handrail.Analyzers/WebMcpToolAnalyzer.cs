using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Handrail.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WebMcpToolAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeName = "Handrail.WebMcpToolAttribute";

    private static readonly Regex WriteVerb = new(
        "(^|_|(?<=[a-z0-9])(?=[A-Z]))(delete|remove|update|create|add|send|pay|submit|order|save|post|set|cancel|transfer|purchase|buy|refund|insert|drop|clear|reset)($|_|(?=[A-Z]))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Secret = new(
        "(password|passwd|pwd|secret|token|apikey|api_key|accesskey|privatekey|credential|authorization|bearer)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Ugc = new(
        "(Comment|Review|Message|Email|Feedback|Reply)",
        RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.UnconfirmedWrite,
        Descriptors.ReadOnlyContradiction,
        Descriptors.UnconfirmedWriteAllowed,
        Descriptors.UntrustedContent,
        Descriptors.SecretInput);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Method);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        var attribute = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeName);
        if (attribute is null)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? method.Locations[0];
        object? Named(string key) => attribute.NamedArguments.Where(n => n.Key == key).Select(n => n.Value.Value).FirstOrDefault();
        bool Flag(string key) => Named(key) is true;

        var ctorName = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
        var toolName = ctorName ?? method.Name;
        var readOnly = Flag("ReadOnly");
        var confirmSet = attribute.NamedArguments.Any(n => n.Key == "Confirm");
        var confirm = Named("Confirm") is int c ? c : 0;
        const int None = 2;
        var allowUnconfirmed = Flag("AllowUnconfirmedWrite");

        if (!readOnly)
        {
            if (confirm == None)
            {
                if (!allowUnconfirmed)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnconfirmedWrite, location, toolName, "uses Confirm.None without AllowUnconfirmedWrite = true"));
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnconfirmedWriteAllowed, location, toolName));
                }
            }
            else if (!Flag("Consequential"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnconfirmedWrite, location, toolName, "is not marked Consequential = true"));
            }
            else if (!confirmSet)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnconfirmedWrite, location, toolName, "does not set Confirm"));
            }
        }
        else
        {
            if (Flag("Consequential"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.ReadOnlyContradiction, location, toolName, "also Consequential = true"));
            }
            else if (WriteVerb.IsMatch(toolName))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.ReadOnlyContradiction, location, toolName, "its name reads like a write"));
            }
        }

        if (!Flag("ReturnsUntrusted"))
        {
            var returned = Unwrap(method.ReturnType);
            if (Ugc.IsMatch(returned.Name) || (returned is INamedTypeSymbol { TypeArguments.Length: > 0 } generic && generic.TypeArguments.Any(a => Ugc.IsMatch(a.Name))))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.UntrustedContent, location, toolName, returned.Name));
            }
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                continue;
            }

            if (Secret.IsMatch(parameter.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.SecretInput, parameter.Locations.FirstOrDefault() ?? location, parameter.Name, toolName));
            }
            else if (parameter.Type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None } type)
            {
                foreach (var property in type.GetMembers().OfType<IPropertySymbol>().Where(p => p.DeclaredAccessibility == Accessibility.Public && Secret.IsMatch(p.Name)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.SecretInput, parameter.Locations.FirstOrDefault() ?? location, parameter.Name + "." + property.Name, toolName));
                }
            }
        }
    }

    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        while (type is INamedTypeSymbol { IsGenericType: true } named &&
               named.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>")
        {
            type = named.TypeArguments[0];
        }

        return type;
    }
}
