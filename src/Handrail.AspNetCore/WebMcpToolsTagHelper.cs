using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Handrail.AspNetCore;

/// <summary>
/// <c>&lt;webmcp-tools /&gt;</c> renders a JSON data block (not executable, so a strict CSP needs no inline allowance) and
/// the runtime as a same-origin module script. Tools with <see cref="WebMcpScope.Page"/> appear only when their class is named in <see cref="Set"/>.
/// </summary>
[HtmlTargetElement("webmcp-tools")]
public sealed class WebMcpToolsTagHelper(
    IWebMcpRegistry registry,
    IAntiforgery antiforgery,
    IOptions<WebMcpOptions> options,
    IOptions<AntiforgeryOptions> antiforgeryOptions,
    ILogger<WebMcpToolsTagHelper> logger) : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary>Comma-separated tool class names (simple or full) whose page-scoped tools belong on this page.</summary>
    public string? Set { get; set; }

    /// <summary>CSP nonce for the script tag. Falls back to <see cref="WebMcpOptions.NonceProvider"/>.</summary>
    public string? Nonce { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var http = ViewContext.HttpContext;
        var opts = options.Value;
        var sets = (Set ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tools = registry.Tools
            .Where(t => t.Scope == WebMcpScope.App || sets.Any(s => s == t.DeclaringType.Name || s == t.DeclaringType.FullName))
            .ToList();

        if (tools.Count > opts.MaxToolsPerPage)
        {
            logger.LogWarning("webmcp page registers {Count} tools; more than {Max} makes agents choose worse (WM-030)", tools.Count, opts.MaxToolsPerPage);
        }

        var baseUrl = http.Request.PathBase + opts.Path;
        var nonce = Nonce ?? opts.NonceProvider?.Invoke(http);
        var config = BuildConfig(baseUrl, tools, antiforgery.GetAndStoreTokens(http).RequestToken, antiforgeryOptions.Value.HeaderName ?? "X-CSRF-TOKEN", opts);

        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.SetHtmlContent(
            $"<script type=\"application/json\" id=\"handrail-config\">{config}</script>" +
            $"<script type=\"module\" src=\"{HtmlEncoder.Default.Encode(baseUrl)}/runtime.js?v={WebMcpRuntime.Version}\"" +
            (string.IsNullOrEmpty(nonce) ? string.Empty : $" nonce=\"{HtmlEncoder.Default.Encode(nonce)}\"") + "></script>");
    }

    // The default encoder escapes < > & ', so no string in the data block, a description for one, can close the script element.
    internal static string BuildConfig(string baseUrl, IReadOnlyList<WebMcpToolDefinition> tools, string? token, string header, WebMcpOptions opts)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteString("endpoint", baseUrl);
            w.WriteStartObject("csrf");
            w.WriteString("header", header);
            w.WriteString("token", token);
            w.WriteEndObject();
            if (opts.ExposedTo.Origins.Count > 0)
            {
                w.WriteStartArray("exposedTo");
                foreach (var origin in opts.ExposedTo.Origins)
                {
                    w.WriteStringValue(origin);
                }

                w.WriteEndArray();
            }

            w.WriteStartArray("tools");
            foreach (var t in tools)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Name);
                w.WriteString("title", t.Title);
                w.WriteString("description", t.Description);
                w.WritePropertyName("inputSchema");
                JsonDocument.Parse(t.InputSchema).RootElement.WriteTo(w);
                if (t.OutputSchema is not null)
                {
                    w.WritePropertyName("outputSchema");
                    JsonDocument.Parse(t.OutputSchema).RootElement.WriteTo(w);
                }

                w.WriteStartObject("annotations");
                if (t.ReadOnly)
                {
                    w.WriteBoolean("readOnlyHint", true);
                }

                if (t.Consequential)
                {
                    w.WriteBoolean("consequentialHint", true);
                }

                if (t.ReturnsUntrusted)
                {
                    w.WriteBoolean("untrustedContentHint", true);
                }

                w.WriteEndObject();
                w.WriteString("confirm", t.IsWrite ? t.Confirm.ToString().ToLowerInvariant() : "none");
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
