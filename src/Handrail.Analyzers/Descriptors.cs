using Microsoft.CodeAnalysis;

namespace Handrail.Analyzers;

internal static class Descriptors
{
    private const string Category = "Handrail.Security";

    private static DiagnosticDescriptor D(string id, string title, string message, DiagnosticSeverity severity) =>
        new(id, title, message, Category, severity, true, helpLinkUri: "https://github.com/Egoushka/handrail/blob/main/docs/analyzers.md#" + id.ToLowerInvariant());

    public static readonly DiagnosticDescriptor UnconfirmedWrite = D("WM0001", "Write tool without confirmation",
        "Tool '{0}' changes state but {1}. A write needs Consequential = true and an explicit Confirm.", DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ReadOnlyContradiction = D("WM0005", "Read-only tool that looks like a write",
        "Tool '{0}' is marked ReadOnly but {1}.", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor UnconfirmedWriteAllowed = D("WM0010", "Write tool runs without confirmation",
        "Tool '{0}' is a write with Confirm.None. Any agent action or injected prompt can run it as the signed-in user.", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor UntrustedContent = D("WM0020", "Tool returns user-generated content",
        "Tool '{0}' returns '{1}', which looks user-generated. Set ReturnsUntrusted = true so agents treat it as data.", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor SecretInput = D("WM0030", "Secret as tool input",
        "Parameter '{0}' of tool '{1}' looks like a secret. Descriptors are published to every agent; never take secrets as tool input.", DiagnosticSeverity.Warning);
}
