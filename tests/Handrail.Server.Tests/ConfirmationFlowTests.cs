using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Handrail.Server.Tests;

/// <summary>The security centre: a write cannot run unless a person confirmed this exact call.</summary>
public class ConfirmationFlowTests
{
    private static async Task<string> Code(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task A_write_needs_a_signed_in_user()
    {
        using var site = new Site();
        var id = site.Notes.All()[0].Id;

        var response = await site.Post("/invoke/delete_note", new { arguments = new { id } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(site.Notes.Find(id));
    }

    [Fact]
    public async Task An_injected_script_that_calls_invoke_directly_gets_403_and_nothing_is_deleted()
    {
        using var site = new Site();
        await site.SignIn();
        var id = site.Notes.All()[0].Id;

        // The script has the page's antiforgery token and the user's cookie, exactly like the real runtime.
        var response = await site.Post("/invoke/delete_note", new { arguments = new { id } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("confirmation_required", await Code(response));
        Assert.NotNull(site.Notes.Find(id));
    }

    [Fact]
    public async Task A_made_up_token_is_refused()
    {
        using var site = new Site();
        await site.SignIn();
        var id = site.Notes.All()[0].Id;

        var response = await site.Post("/invoke/delete_note", new { arguments = new { id }, confirmation = "a.1.b" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("confirmation_invalid", await Code(response));
    }

    [Fact]
    public async Task A_confirmed_call_runs_once()
    {
        using var site = new Site();
        await site.SignIn();
        var id = site.Notes.All()[0].Id;
        var token = await site.MintToken("delete_note", new { id });

        var first = await site.Post("/invoke/delete_note", new { arguments = new { id }, confirmation = token });
        var replay = await site.Post("/invoke/delete_note", new { arguments = new { id }, confirmation = token });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Null(site.Notes.Find(id));
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal("confirmation_invalid", await Code(replay));
    }

    [Fact]
    public async Task A_token_does_not_cover_different_arguments()
    {
        using var site = new Site();
        await site.SignIn();
        var notes = site.Notes.All();
        var token = await site.MintToken("delete_note", new { id = notes[0].Id });

        var response = await site.Post("/invoke/delete_note", new { arguments = new { id = notes[1].Id }, confirmation = token });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(site.Notes.Find(notes[1].Id));
    }

    [Fact]
    public async Task A_token_for_one_tool_does_not_work_for_another()
    {
        using var site = new Site();
        await site.SignIn();
        var token = await site.MintToken("add_note", new { title = "x" });

        var response = await site.Post("/invoke/delete_note", new { arguments = new { id = site.Notes.All()[0].Id }, confirmation = token });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_is_not_offered_for_read_tools_or_to_anonymous_callers()
    {
        using var site = new Site();

        var anonymous = await site.Post("/confirm/delete_note", new { arguments = new { id = Guid.NewGuid() } });
        await site.SignIn();
        var readTool = await site.Post("/confirm/search_notes", new { arguments = new { query = "a" } });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, readTool.StatusCode);
    }

    [Fact]
    public async Task A_confirmed_add_validates_its_input_after_the_token()
    {
        using var site = new Site();
        await site.SignIn();
        var token = await site.MintToken("add_note", new { title = "" });

        var response = await site.Post("/invoke/add_note", new { arguments = new { title = "" }, confirmation = token });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
