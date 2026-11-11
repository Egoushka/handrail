using Xunit;

namespace Handrail.Tests;

public class AnalyzerTests
{
    private static async Task<string[]> Ids(string member, string extra = "")
    {
        var diagnostics = await Compile.Analyze($"using Handrail; {extra} public class T {{ {member} }}");
        return diagnostics.Select(d => d.Id).OrderBy(i => i).ToArray();
    }

    [Fact]
    public async Task A_write_without_consequential_is_wm0001() =>
        Assert.Equal(["WM0001"], await Ids("""[WebMcpTool("x", Confirm = Confirm.Page)] public void M() {}"""));

    [Fact]
    public async Task A_write_without_confirm_is_wm0001() =>
        Assert.Equal(["WM0001"], await Ids("""[WebMcpTool("x", Consequential = true)] public void M() {}"""));

    [Fact]
    public async Task A_confirmed_consequential_write_is_clean() =>
        Assert.Empty(await Ids("""[WebMcpTool("x", Consequential = true, Confirm = Confirm.Page)] public void M() {}"""));

    [Fact]
    public async Task Confirm_none_needs_the_explicit_opt_out() =>
        Assert.Equal(["WM0001"], await Ids("""[WebMcpTool("x", Confirm = Confirm.None)] public void M() {}"""));

    [Fact]
    public async Task Allowed_unconfirmed_write_is_a_warning_wm0010() =>
        Assert.Equal(["WM0010"], await Ids("""[WebMcpTool("x", Confirm = Confirm.None, AllowUnconfirmedWrite = true)] public void M() {}"""));

    [Fact]
    public async Task A_read_only_tool_needs_nothing() =>
        Assert.Empty(await Ids("""[WebMcpTool("search", ReadOnly = true)] public void M() {}"""));

    [Theory]
    [InlineData("""[WebMcpTool("delete_note", ReadOnly = true)]""")]
    [InlineData("""[WebMcpTool("lookup", ReadOnly = true, Consequential = true)]""")]
    public async Task Read_only_that_looks_like_a_write_is_wm0005(string attribute) =>
        Assert.Equal(["WM0005"], await Ids(attribute + " public void M() {}"));

    [Fact]
    public async Task Returning_user_generated_content_without_the_flag_is_wm0020() =>
        Assert.Equal(["WM0020"], await Ids("""[WebMcpTool("list", ReadOnly = true)] public System.Collections.Generic.List<Comment> M() => new();""", "public record Comment(string Text);"));

    [Fact]
    public async Task Returns_untrusted_silences_wm0020() =>
        Assert.Empty(await Ids("""[WebMcpTool("list", ReadOnly = true, ReturnsUntrusted = true)] public System.Collections.Generic.List<Comment> M() => new();""", "public record Comment(string Text);"));

    [Fact]
    public async Task Secrets_as_input_are_wm0030() =>
        Assert.Equal(["WM0030"], await Ids("""[WebMcpTool("x", ReadOnly = true)] public void M(string apiKey) {}"""));

    [Fact]
    public async Task A_cancellation_token_is_not_a_secret() =>
        Assert.Empty(await Ids("""[WebMcpTool("x", ReadOnly = true)] public void M(System.Threading.CancellationToken cancellationToken) {}"""));

    [Fact]
    public async Task Secret_properties_of_an_input_record_are_found() =>
        Assert.Equal(["WM0030"], await Ids("""[WebMcpTool("x", ReadOnly = true)] public void M(Login l) {}""", "public record Login(string User, string Password);"));
}
