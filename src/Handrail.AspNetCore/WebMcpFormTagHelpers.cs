using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Handrail.AspNetCore;

/// <summary>
/// Declarative WebMCP: turns an ordinary form into a tool. Auto-submit is only allowed on a read-only form (WM0011):
/// a form that changes state is always submitted by the person.
/// </summary>
[HtmlTargetElement("form", Attributes = "webmcp-tool")]
public sealed partial class WebMcpFormTagHelper : TagHelper
{
    [HtmlAttributeName("webmcp-tool")]
    public string Tool { get; set; } = string.Empty;

    [HtmlAttributeName("webmcp-description")]
    public string? Description { get; set; }

    [HtmlAttributeName("webmcp-readonly")]
    public bool ReadOnly { get; set; }

    [HtmlAttributeName("webmcp-autosubmit")]
    public bool AutoSubmit { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!ToolName().IsMatch(Tool))
        {
            throw new InvalidOperationException($"WM0002: webmcp-tool '{Tool}' must be 1-128 characters of A-Z a-z 0-9 _ . -.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new InvalidOperationException($"WM0006: form tool '{Tool}' needs webmcp-description; agents choose tools by it.");
        }

        if (AutoSubmit && !ReadOnly)
        {
            throw new InvalidOperationException($"WM0011: form tool '{Tool}' asks for webmcp-autosubmit but is not webmcp-readonly. A form that changes state is submitted by the person, never by the agent.");
        }

        output.Attributes.SetAttribute("toolname", Tool);
        output.Attributes.SetAttribute("tooldescription", Description);
        if (AutoSubmit)
        {
            output.Attributes.SetAttribute("toolautosubmit", string.Empty);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex ToolName();
}

/// <summary>Describes a form field for agents: <c>toolparamdescription</c>.</summary>
[HtmlTargetElement("input", Attributes = "webmcp-param")]
[HtmlTargetElement("select", Attributes = "webmcp-param")]
[HtmlTargetElement("textarea", Attributes = "webmcp-param")]
public sealed class WebMcpParamTagHelper : TagHelper
{
    [HtmlAttributeName("webmcp-param")]
    public string Description { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output) =>
        output.Attributes.SetAttribute("toolparamdescription", Description);
}
