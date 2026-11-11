using System.Text;
using System.Text.RegularExpressions;

namespace Handrail.Generators;

internal static class Names
{
    public static string SnakeCase(string name)
    {
        if (name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5)
        {
            name = name.Substring(0, name.Length - 5);
        }

        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    public static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    public static string Humanize(string toolName)
    {
        var words = toolName.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ').Trim();
        return words.Length == 0 ? toolName : char.ToUpperInvariant(words[0]) + words.Substring(1);
    }

    public static bool IsValidToolName(string name) =>
        name.Length is >= 1 and <= 128 && Regex.IsMatch(name, "^[A-Za-z0-9_.-]+$");

    public static string Sanitize(string? assemblyName)
    {
        var sb = new StringBuilder();
        foreach (var c in assemblyName ?? "App")
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sb.Length == 0 || char.IsDigit(sb[0]) ? "_" + sb : sb.ToString();
    }

    public static string Json(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
