using Handrail.AspNetCore;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Handrail.Server.Tests;

public class UnsafeTools
{
#pragma warning disable WM0001
    [WebMcpTool("danger", Confirm = Confirm.None, Description = "Writes without asking.")]
    public void Danger() { }

    [WebMcpTool("browser_only", Consequential = true, Confirm = Confirm.Browser, Description = "Leans on the browser prompt.")]
    public void BrowserOnly() { }

    [WebMcpTool("quiet_write", Confirm = Confirm.Page, Description = "A write that forgot Consequential.")]
    public void QuietWrite() { }
#pragma warning restore WM0001
}

public class ConfigurationTests
{
    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("http://agent.example")]
    [InlineData("https://agent.example/path")]
    [InlineData("https://agent.example/")]
    [InlineData("agent.example")]
    public void Exposure_accepts_only_explicit_https_origins(string origin) =>
        Assert.Throws<ArgumentException>(() => WebMcpExposure.To(origin));

    [Fact]
    public void Exposure_accepts_an_explicit_https_origin() =>
        Assert.Equal(["https://agent.example"], WebMcpExposure.To("https://agent.example").Origins);

    [Theory]
    [InlineData("danger", "AllowUnconfirmedWrite")]
    [InlineData("browser_only", "AllowBrowserConfirm")]
    [InlineData("quiet_write", "Consequential")]
    public void The_registry_refuses_unsafe_writes_even_when_the_analyzer_was_off(string tool, string mentions)
    {
        var only = WebMcpRegistry.FromAssembly(typeof(UnsafeTools).Assembly).Where(t => t.Name == tool).ToList();

        var error = Assert.Throws<InvalidOperationException>(() => new WebMcpRegistry([new WebMcpToolSource(only)], Microsoft.Extensions.Options.Options.Create(new WebMcpOptions())));

        Assert.Contains("WM0001", error.Message);
        Assert.Contains(mentions, error.Message);
    }

    [Fact]
    public void A_description_cannot_close_the_script_element()
    {
        var tool = new WebMcpToolDefinition
        {
            Name = "x", Title = "X", Description = "</script><script>alert(1)</script>",
            InputSchema = """{"type":"object","properties":{"a":{"type":"string","description":"</script>"}}}""",
            DeclaringType = typeof(object), Invoke = (_, _, _) => default,
        };

        var json = WebMcpToolsTagHelper.BuildConfig("/_webmcp", [tool], "t", "X-CSRF-TOKEN", new WebMcpOptions());

        Assert.DoesNotContain("</script", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", json);
    }

    private static TagHelperOutput Run(WebMcpFormTagHelper helper)
    {
        var output = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "id"), output);
        return output;
    }

    [Fact]
    public void A_form_that_is_not_read_only_cannot_auto_submit()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run(new WebMcpFormTagHelper { Tool = "pay", Description = "Pay an invoice", AutoSubmit = true }));

        Assert.Contains("WM0011", error.Message);
    }

    [Fact]
    public void A_read_only_form_may_auto_submit_and_a_plain_form_does_not()
    {
        var auto = Run(new WebMcpFormTagHelper { Tool = "find", Description = "Find things", ReadOnly = true, AutoSubmit = true });
        var plain = Run(new WebMcpFormTagHelper { Tool = "pay", Description = "Pay an invoice" });

        Assert.True(auto.Attributes.ContainsName("toolautosubmit"));
        Assert.Equal("find", auto.Attributes["toolname"].Value);
        Assert.False(plain.Attributes.ContainsName("toolautosubmit"));
    }

    [Fact]
    public void A_form_tool_needs_a_valid_name_and_a_description()
    {
        Assert.Throws<InvalidOperationException>(() => Run(new WebMcpFormTagHelper { Tool = "bad name", Description = "x" }));
        Assert.Throws<InvalidOperationException>(() => Run(new WebMcpFormTagHelper { Tool = "ok" }));
    }
}
