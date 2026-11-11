using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Handrail.Generators;

/// <summary>Builds JSON Schema text for C# types at compile time. Anything it cannot describe is reported, not guessed.</summary>
internal sealed class SchemaBuilder
{
    private readonly List<string> _problems = new();
    private readonly bool _forOutput;

    public SchemaBuilder(bool forOutput) => _forOutput = forOutput;

    public IReadOnlyList<string> Problems => _problems;

    public static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>Returns schema members (key, raw JSON) for the type, or null when unsupported (the reason is in <see cref="Problems"/>).</summary>
    public List<KeyValuePair<string, string>>? Build(ITypeSymbol type, string where, HashSet<ITypeSymbol>? stack = null)
    {
        stack ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return Build(nullable.TypeArguments[0], where, stack);
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return Simple("boolean");
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return Simple("integer");
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return Simple("number");
            case SpecialType.System_String: return Simple("string");
            case SpecialType.System_Char: return new() { Pair("type", "\"string\""), Pair("minLength", "1"), Pair("maxLength", "1") };
            case SpecialType.System_DateTime: return Simple("string", "date-time");
            case SpecialType.System_Object:
                return Unsupported(type, where, "'object' has no schema; use a record or JsonElement");
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            var names = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).Select(f => Names.Json(f.Name));
            return new() { Pair("type", "\"string\""), Pair("enum", "[" + string.Join(",", names) + "]") };
        }

        if (type is IArrayTypeSymbol array)
        {
            return Array(array.ElementType, where, stack);
        }

        var full = type.OriginalDefinition.ToDisplayString();
        switch (full)
        {
            case "System.Guid": return Simple("string", "uuid");
            case "System.DateTimeOffset": return Simple("string", "date-time");
            case "System.DateOnly": return Simple("string", "date");
            case "System.TimeOnly": return Simple("string", "time");
            case "System.Uri": return Simple("string", "uri");
            case "System.Text.Json.JsonElement":
            case "System.Text.Json.Nodes.JsonNode":
            case "System.Text.Json.Nodes.JsonObject":
                return new();
            case "System.TimeSpan":
                return Unsupported(type, where, "TimeSpan serializes in a non-ISO format; use seconds as a number");
        }

        if (type is INamedTypeSymbol named)
        {
            if (full is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IReadOnlyList<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.HashSet<T>"
                or "System.Collections.Generic.ISet<T>" or "System.Collections.Immutable.ImmutableArray<T>")
            {
                return Array(named.TypeArguments[0], where, stack);
            }

            if (full is "System.Collections.Generic.Dictionary<TKey, TValue>" or "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                if (named.TypeArguments[0].SpecialType != SpecialType.System_String)
                {
                    return Unsupported(type, where, "dictionary keys must be strings");
                }

                var value = Build(named.TypeArguments[1], where, stack);
                return value is null ? null : new() { Pair("type", "\"object\""), Pair("additionalProperties", Render(value)) };
            }

            if (named.TypeKind is TypeKind.Class or TypeKind.Struct && !named.IsAbstract && named.SpecialType == SpecialType.None)
            {
                return Object(named, where, stack);
            }
        }

        return Unsupported(type, where, "no JSON Schema mapping");
    }

    public static string Render(List<KeyValuePair<string, string>> members) =>
        "{" + string.Join(",", members.Select(m => Names.Json(m.Key) + ":" + m.Value)) + "}";

    public static void ApplyConstraints(ISymbol symbol, List<KeyValuePair<string, string>> schema)
    {
        var isString = schema.Any(m => m.Key == "type" && m.Value == "\"string\"");
        var isArray = schema.Any(m => m.Key == "type" && m.Value == "\"array\"");
        var isNumeric = schema.Any(m => m.Key == "type" && (m.Value == "\"integer\"" || m.Value == "\"number\""));

        foreach (var attribute in symbol.GetAttributes())
        {
            var args = attribute.ConstructorArguments;
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.ComponentModel.DataAnnotations.RangeAttribute" when isNumeric && args.Length >= 2 && args[0].Value is IConvertible lo && args[1].Value is IConvertible hi:
                    Set(schema, "minimum", Number(lo.ToDouble(CultureInfo.InvariantCulture)));
                    Set(schema, "maximum", Number(hi.ToDouble(CultureInfo.InvariantCulture)));
                    break;
                case "System.ComponentModel.DataAnnotations.StringLengthAttribute" when isString && args.Length >= 1 && args[0].Value is int max:
                    Set(schema, "maxLength", max.ToString(CultureInfo.InvariantCulture));
                    var min = attribute.NamedArguments.FirstOrDefault(n => n.Key == "MinimumLength").Value.Value;
                    if (min is int minValue && minValue > 0)
                    {
                        Set(schema, "minLength", minValue.ToString(CultureInfo.InvariantCulture));
                    }

                    break;
                case "System.ComponentModel.DataAnnotations.MinLengthAttribute" when args.Length >= 1 && args[0].Value is int minLen:
                    Set(schema, isArray ? "minItems" : "minLength", minLen.ToString(CultureInfo.InvariantCulture));
                    break;
                case "System.ComponentModel.DataAnnotations.MaxLengthAttribute" when args.Length >= 1 && args[0].Value is int maxLen:
                    Set(schema, isArray ? "maxItems" : "maxLength", maxLen.ToString(CultureInfo.InvariantCulture));
                    break;
                case "System.ComponentModel.DataAnnotations.RegularExpressionAttribute" when isString && args.Length >= 1 && args[0].Value is string pattern:
                    Set(schema, "pattern", Names.Json(pattern));
                    break;
                case "System.ComponentModel.DataAnnotations.RequiredAttribute" when isString:
                    if (!schema.Any(m => m.Key == "minLength"))
                    {
                        Set(schema, "minLength", "1");
                    }

                    break;
                case "System.ComponentModel.DescriptionAttribute" when args.Length >= 1 && args[0].Value is string text:
                    Set(schema, "description", Names.Json(text));
                    break;
            }
        }
    }

    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void Set(List<KeyValuePair<string, string>> schema, string key, string raw)
    {
        schema.RemoveAll(m => m.Key == key);
        schema.Add(Pair(key, raw));
    }

    private static KeyValuePair<string, string> Pair(string key, string raw) => new(key, raw);

    private static List<KeyValuePair<string, string>> Simple(string type, string? format = null)
    {
        var list = new List<KeyValuePair<string, string>> { Pair("type", Names.Json(type)) };
        if (format is not null)
        {
            list.Add(Pair("format", Names.Json(format)));
        }

        return list;
    }

    private List<KeyValuePair<string, string>>? Unsupported(ITypeSymbol type, string where, string reason)
    {
        _problems.Add($"{where}: type '{type.ToDisplayString()}' is not supported ({reason}).");
        return null;
    }

    private List<KeyValuePair<string, string>>? Array(ITypeSymbol element, string where, HashSet<ITypeSymbol> stack)
    {
        var items = Build(element, where, stack);
        return items is null ? null : new() { Pair("type", "\"array\""), Pair("items", Render(items)) };
    }

    private List<KeyValuePair<string, string>>? Object(INamedTypeSymbol type, string where, HashSet<ITypeSymbol> stack)
    {
        if (!stack.Add(type))
        {
            return Unsupported(type, where, "recursive types cannot be expressed without $ref");
        }

        var properties = new List<string>();
        var required = new List<string>();
        var ok = true;

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null)
            {
                continue;
            }

            if (!_forOutput && property.SetMethod is null)
            {
                continue;
            }

            if (property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonIgnoreAttribute"))
            {
                continue;
            }

            var renamed = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonPropertyNameAttribute")?
                .ConstructorArguments.FirstOrDefault().Value as string;
            var name = renamed ?? Names.CamelCase(property.Name);

            var schema = Build(property.Type, $"{where}.{property.Name}", stack);
            if (schema is null)
            {
                ok = false;
                continue;
            }

            ApplyConstraints(property, schema);
            properties.Add(Names.Json(name) + ":" + Render(schema));

            var isRequired = property.IsRequired ||
                property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DataAnnotations.RequiredAttribute") ||
                (_forOutput && !IsNullable(property.Type));
            if (isRequired)
            {
                required.Add(Names.Json(name));
            }
        }

        stack.Remove(type);
        if (!ok)
        {
            return null;
        }

        var result = new List<KeyValuePair<string, string>> { Pair("type", "\"object\""), Pair("properties", "{" + string.Join(",", properties) + "}") };
        if (required.Count > 0)
        {
            result.Add(Pair("required", "[" + string.Join(",", required) + "]"));
        }

        return result;
    }

    public static string? EnumName(ITypeSymbol type, object? value) =>
        type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value))?.Name;

}
