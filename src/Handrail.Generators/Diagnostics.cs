using Microsoft.CodeAnalysis;

namespace Handrail.Generators;

internal static class GeneratorDiagnostics
{
    private const string Category = "Handrail";

    public static readonly DiagnosticDescriptor InvalidName = new("WM0002", "Invalid tool name",
        "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor DuplicateName = new("WM0003", "Duplicate tool name",
        "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedType = new("WM0004", "Unsupported parameter type",
        "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MissingDescription = new("WM0006", "Tool has no description",
        "{0}", Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UnusableMethod = new("WM0007", "Method cannot be a tool",
        "{0}", Category, DiagnosticSeverity.Error, true);

    public static DiagnosticDescriptor ById(string id) => id switch
    {
        "WM0002" => InvalidName,
        "WM0003" => DuplicateName,
        "WM0004" => UnsupportedType,
        "WM0006" => MissingDescription,
        _ => UnusableMethod,
    };
}
