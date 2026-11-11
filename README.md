# Handrail

A safe guide for agents on your page. Handrail turns C# methods into
[WebMCP](https://webmachinelearning.github.io/webmcp/) tools for ASP.NET Core and Blazor: one attribute is the
source of truth for the browser tool, its JSON Schema, and the server-side check that guards it.

Status: preview, work in progress. Nothing is published to NuGet yet.

```csharp
public class NotesTools(INoteStore notes)
{
    /// <summary>Search the user's notes by text. Use before get_note.</summary>
    [WebMcpTool("search_notes", ReadOnly = true)]
    public Task<IReadOnlyList<NoteSummary>> Search(
        [Description("Words to look for")] string query,
        [Range(1, 20)] int limit = 5) => notes.SearchAsync(query, limit);

    /// <summary>Permanently delete a note.</summary>
    [WebMcpTool("delete_note", Consequential = true, Confirm = Confirm.Page)]
    public Task Delete(Guid id) => notes.DeleteAsync(id);
}
```

Writes need confirmation: a page dialog plus a one-time server token. A write without `Confirm` fails the build.

Packages: `Handrail` (attributes, descriptor model, source generator, analyzers) and `Handrail.AspNetCore`
(invoke endpoint, tag helper, JavaScript runtime). Licence: MIT.
