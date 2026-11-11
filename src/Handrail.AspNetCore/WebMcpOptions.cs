namespace Handrail.AspNetCore;

/// <summary>Which origins may see the tools. The default registers with no <c>exposedTo</c>, so the browser shows them to the page's own origin only.</summary>
public sealed class WebMcpExposure
{
    private WebMcpExposure(IReadOnlyList<string> origins) => Origins = origins;

    public static WebMcpExposure SameOriginOnly { get; } = new([]);

    public IReadOnlyList<string> Origins { get; }

    /// <summary>Explicit HTTPS origins such as <c>https://agent.example</c>. Wildcards, http and paths are refused.</summary>
    public static WebMcpExposure To(params string[] origins)
    {
        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || origin.Contains('*') || uri.PathAndQuery != "/" || origin.EndsWith('/') || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException($"'{origin}' is not an explicit https origin (no wildcard, http, path or trailing slash).", nameof(origins));
            }
        }

        return new WebMcpExposure(origins.ToArray());
    }
}

public sealed class WebMcpOptions
{
    public WebMcpExposure ExposedTo { get; set; } = WebMcpExposure.SameOriginOnly;

    /// <summary>A tool that is not read-only needs a signed-in user. On by default.</summary>
    public bool RequireAuthenticatedForWrites { get; set; } = true;

    /// <summary>Allows <see cref="Confirm.Browser"/>. Off: the runtime cannot tell whether the browser honours <c>consequentialHint</c>.</summary>
    public bool AllowBrowserConfirm { get; set; }

    /// <summary>Calls per minute per user and tool. 0 turns the limit off.</summary>
    public int RateLimitPerMinute { get; set; } = 30;

    /// <summary>A page that registers more tools than this gets a log warning (Lighthouse flags more than 40).</summary>
    public int MaxToolsPerPage { get; set; } = 40;

    public string Path { get; set; } = "/_webmcp";

    /// <summary>Signs confirmation tokens. Set the same key on every instance; when null each process makes its own.</summary>
    public byte[]? ConfirmationKey { get; set; }

    /// <summary>Supplies the CSP nonce for the script tags when the tag helper is not given one.</summary>
    public Func<Microsoft.AspNetCore.Http.HttpContext, string?>? NonceProvider { get; set; }
}
