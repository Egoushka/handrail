using System.Text.Json;
using System.Text.Json.Serialization;

namespace Handrail;

/// <summary>The one serializer configuration used for tool inputs and results, so schema and wire format agree.</summary>
public static class WebMcpJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Result of a tool that returns nothing.</summary>
    public const string Ok = "{\"ok\":true}";

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Result types are known to the generator; see docs/aot.md.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Result types are known to the generator; see docs/aot.md.")]
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
