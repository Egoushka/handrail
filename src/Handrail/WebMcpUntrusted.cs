using System.Text;

namespace Handrail;

/// <summary>Marks tool output that carries user-generated text, so an agent can tell data from instructions.</summary>
public static class WebMcpUntrusted
{
    public const string OpenTag = "<untrusted-content>";
    public const string CloseTag = "</untrusted-content>";

    /// <summary>Strips control characters (except tab and newline), defuses the closing tag, and wraps the text.</summary>
    public static string Wrap(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c) || c is '\n' or '\t')
            {
                clean.Append(c);
            }
        }

        var body = clean.ToString().Replace(CloseTag, "<\\/untrusted-content>", StringComparison.OrdinalIgnoreCase);
        return OpenTag + "\n" + body + "\n" + CloseTag;
    }
}
