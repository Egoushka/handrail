namespace Handrail;

/// <summary>How a write tool asks the user before it runs.</summary>
public enum Confirm
{
    /// <summary>The Handrail dialog in the page, backed by a one-time server token. The default and the safe choice.</summary>
    Page = 0,

    /// <summary>Rely on the browser's own prompt. Off unless <c>WebMcpOptions.AllowBrowserConfirm</c> is set.</summary>
    Browser = 1,

    /// <summary>No confirmation. Needs <see cref="WebMcpToolAttribute.AllowUnconfirmedWrite"/> and raises WM0010.</summary>
    None = 2,
}

/// <summary>Where a tool is registered.</summary>
public enum WebMcpScope
{
    /// <summary>On every page that renders the Handrail script tag.</summary>
    App = 0,

    /// <summary>Only on pages whose tag names the tool set.</summary>
    Page = 1,
}

/// <summary>Marks a method as a WebMCP tool. The source generator reads it; nothing is looked up by reflection at runtime.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WebMcpToolAttribute : Attribute
{
    /// <param name="name">Tool name, 1-128 characters of A-Z a-z 0-9 _ . -. Defaults to the snake_case method name.</param>
    public WebMcpToolAttribute(string? name = null) => Name = name;

    public string? Name { get; }

    public string? Title { get; set; }

    /// <summary>Overrides the XML summary as the description agents read.</summary>
    public string? Description { get; set; }

    /// <summary>The tool never changes state. Sends <c>readOnlyHint</c>. A method without it is a write.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>The tool has side effects that are hard to undo. Sends <c>consequentialHint</c>.</summary>
    public bool Consequential { get; set; }

    /// <summary>The tool returns user-generated data. Sends <c>untrustedContentHint</c> and wraps the output.</summary>
    public bool ReturnsUntrusted { get; set; }

    public Confirm Confirm { get; set; }

    /// <summary>Permits <see cref="Confirm.None"/> on a write.</summary>
    public bool AllowUnconfirmedWrite { get; set; }

    public WebMcpScope Scope { get; set; }
}
