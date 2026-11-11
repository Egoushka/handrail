using System.Text.Json;

namespace Handrail;

/// <summary>Runs a tool. Binds <paramref name="arguments"/> to the method's parameters and returns the JSON text the browser tool resolves with.</summary>
public delegate ValueTask<string> WebMcpInvoker(IServiceProvider services, JsonElement arguments, CancellationToken cancellationToken);

/// <summary>An <c>[Authorize]</c> attribute copied at build time, so the endpoint can re-check it without reflection.</summary>
public sealed record WebMcpAuthorizeRule(string? Policy, string? Roles, string? AuthenticationSchemes);

/// <summary>Everything the runtime needs to know about one tool. Emitted by the source generator as plain data.</summary>
public sealed class WebMcpToolDefinition
{
    public required string Name { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    /// <summary>JSON Schema for the arguments, a constant string built at compile time.</summary>
    public required string InputSchema { get; init; }

    /// <summary>JSON Schema for the result, or null when the result is not an object.</summary>
    public string? OutputSchema { get; init; }

    public bool ReadOnly { get; init; }

    public bool Consequential { get; init; }

    public bool ReturnsUntrusted { get; init; }

    public Confirm Confirm { get; init; }

    public bool AllowUnconfirmedWrite { get; init; }

    public WebMcpScope Scope { get; init; }

    public required Type DeclaringType { get; init; }

    public IReadOnlyList<WebMcpAuthorizeRule> Authorize { get; init; } = [];

    public bool AllowAnonymous { get; init; }

    public required WebMcpInvoker Invoke { get; init; }

    /// <summary>A write is any tool not marked read-only.</summary>
    public bool IsWrite => !ReadOnly;
}

/// <summary>Implemented by the generated class that lists an assembly's tools.</summary>
public interface IWebMcpToolSet
{
    IReadOnlyList<WebMcpToolDefinition> Tools { get; }
}

/// <summary>Points at the generated <see cref="IWebMcpToolSet"/> of an assembly. Emitted by the source generator.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class WebMcpToolSetAttribute : Attribute
{
    public WebMcpToolSetAttribute(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type toolSetType) => ToolSetType = toolSetType;

    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ToolSetType { get; }
}
