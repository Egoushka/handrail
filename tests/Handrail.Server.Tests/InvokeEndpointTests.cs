using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Handrail.Server.Tests;

public class InvokeEndpointTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> Code(HttpResponseMessage r) => (await Json(r)).GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task The_page_publishes_descriptors_with_the_annotations_the_spec_names()
    {
        using var site = new Site();

        var config = await site.Config();
        var tools = config.GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);

        Assert.True(tools["search_notes"].GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.True(tools["search_notes"].GetProperty("annotations").GetProperty("untrustedContentHint").GetBoolean());
        Assert.True(tools["delete_note"].GetProperty("annotations").GetProperty("consequentialHint").GetBoolean());
        Assert.Equal("page", tools["delete_note"].GetProperty("confirm").GetString());
        Assert.Equal("none", tools["search_notes"].GetProperty("confirm").GetString());
        Assert.False(config.TryGetProperty("exposedTo", out _));
    }

    [Fact]
    public async Task The_page_loads_the_runtime_as_a_module_with_no_inline_script()
    {
        using var site = new Site();

        var html = await site.Client.GetStringAsync("/");

        Assert.Contains("<script type=\"module\" src=\"/_webmcp/runtime.js?v=", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("toolname=\"find_notes_form\"", html);
        Assert.Contains("toolautosubmit", html);
        Assert.Contains("toolparamdescription=\"Word to look for", html);
    }

    [Fact]
    public async Task The_runtime_is_served_as_javascript()
    {
        using var site = new Site();

        var response = await site.Client.GetAsync("/_webmcp/runtime.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/javascript", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("document.modelContext", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_read_tool_works_without_signing_in_and_wraps_untrusted_output()
    {
        using var site = new Site();

        var response = await site.Post("/invoke/search_notes", new { arguments = new { query = "milk" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await Json(response)).GetProperty("result").GetString()!;
        Assert.StartsWith("<untrusted-content>\n", result);
        Assert.Contains("Groceries", result);
    }

    [Fact]
    public async Task Invoke_is_post_only()
    {
        using var site = new Site();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await site.Client.GetAsync("/_webmcp/invoke/search_notes")).StatusCode);
    }

    [Fact]
    public async Task A_call_without_the_antiforgery_header_is_refused()
    {
        using var site = new Site();

        var response = await site.Post("/invoke/search_notes", new { arguments = new { query = "milk" } }, csrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("antiforgery", await Code(response));
    }

    [Fact]
    public async Task A_form_post_is_refused_so_a_cross_site_form_cannot_call_it()
    {
        using var site = new Site();

        var response = await site.Post("/invoke/search_notes", new { }, tweak: r => r.Content = new StringContent("arguments=1", Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_cross_site_fetch_is_refused()
    {
        using var site = new Site();

        var response = await site.Post("/invoke/search_notes", new { arguments = new { query = "m" } }, tweak: r => r.Headers.Add("Sec-Fetch-Site", "cross-site"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden_origin", await Code(response));
    }

    [Fact]
    public async Task An_unknown_tool_is_404()
    {
        using var site = new Site();

        Assert.Equal(HttpStatusCode.NotFound, (await site.Post("/invoke/nope", new { })).StatusCode);
    }

    [Fact]
    public async Task Bad_input_is_a_400_with_the_reason()
    {
        using var site = new Site();

        var missing = await site.Post("/invoke/search_notes", new { arguments = new { } });
        var extra = await site.Post("/invoke/search_notes", new { arguments = new { query = "a", sneaky = 1 } });
        var range = await site.Post("/invoke/search_notes", new { arguments = new { query = "a", limit = 99 } });

        Assert.All(new[] { missing, extra, range }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Contains("'query' is required", (await Json(missing)).GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("Unknown argument 'sneaky'", (await Json(extra)).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_failing_tool_returns_no_internals()
    {
        using var site = new Site();

        var response = await site.Post("/invoke/boom", new { arguments = new { } });
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("InvalidOperation", text);
        Assert.Equal("tool_failed", await Code(response));
    }

    [Fact]
    public async Task The_authorize_attribute_is_enforced_by_the_endpoint()
    {
        using var site = new Site();
        var anonymous = await site.Post("/invoke/admin_report", new { arguments = new { } });
        await site.SignIn();
        var wrongRole = await site.Post("/invoke/admin_report", new { arguments = new { } });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
    }

    [Fact]
    public async Task Calls_are_rate_limited_per_tool_and_user()
    {
        using var site = new Site(o => o.RateLimitPerMinute = 3);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await site.Post("/invoke/ping", new { arguments = new { } })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
