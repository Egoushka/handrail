using System.Text.Json;

namespace Handrail;

/// <summary>Argument binding used by generated invokers. Public because generated code lives in the user's assembly.</summary>
public static class WebMcpInput
{
    /// <summary>The arguments must be a JSON object and carry no member outside <paramref name="known"/>.</summary>
    public static void RejectUnknown(JsonElement args, params string[] known)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            throw new WebMcpInputException("Arguments must be a JSON object.");
        }

        foreach (var property in args.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) < 0)
            {
                throw new WebMcpInputException($"Unknown argument '{property.Name}'.");
            }
        }
    }

    public static T Required<T>(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            throw new WebMcpInputException($"Argument '{name}' is required.");
        }

        return Read<T>(value, name);
    }

    public static T Optional<T>(JsonElement args, string name, T defaultValue)
    {
        if (!args.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        return Read<T>(value, name);
    }

    public static void CheckRange(string name, IConvertible? value, double min, double max)
    {
        if (value is null)
        {
            return;
        }

        var number = value.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
        if (number < min || number > max)
        {
            throw new WebMcpInputException($"Argument '{name}' must be between {min} and {max}.");
        }
    }

    public static void CheckLength(string name, string? value, int min, int max)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length < min || value.Length > max)
        {
            throw new WebMcpInputException($"Argument '{name}' must be {min} to {max} characters.");
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Parameter types are known to the generator.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Parameter types are known to the generator.")]
    private static T Read<T>(JsonElement value, string name)
    {
        try
        {
            return value.Deserialize<T>(WebMcpJson.Options)
                ?? throw new WebMcpInputException($"Argument '{name}' must not be null.");
        }
        catch (JsonException)
        {
            throw new WebMcpInputException($"Argument '{name}' has the wrong type or shape.");
        }
    }
}
