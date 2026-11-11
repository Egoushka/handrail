# Handrail

A safe guide for agents on your page. Handrail turns C# methods into
[WebMCP](https://webmachinelearning.github.io/webmcp/) tools for ASP.NET Core: one attribute is the source of truth
for the browser tool, its JSON Schema, and the server-side checks that guard it.

Status: preview, work in progress. Not published to NuGet yet. WebMCP is a draft (Community Group report, 10 Nov
2026) and Chrome support is early preview, so the browser runtime is one small file that follows the spec.

## Use

```csharp
public class NotesTools(INoteStore notes)
{
    /// <summary>Search the user's notes by text. Use before get_note.</summary>
    [WebMcpTool("search_notes", ReadOnly = true, ReturnsUntrusted = true)]
    public IReadOnlyList<NoteSummary> Search(
        [Description("Words to look for")] string query,
        [Range(1, 20)] int limit = 5) => notes.Search(query, limit);

    /// <summary>Permanently delete a note.</summary>
    [WebMcpTool("delete_note", Consequential = true, Confirm = Confirm.Page)]
    public bool Delete(Guid id) => notes.Delete(id);
}
```

```csharp
builder.Services.AddWebMcp();
builder.Services.AddWebMcpTools<NotesTools>();
app.MapWebMcp();            // POST /_webmcp/invoke/{tool}, POST /_webmcp/confirm/{tool}, GET /_webmcp/runtime.js
```

```html
@addTagHelper *, Handrail.AspNetCore
<webmcp-tools />            <!-- in the layout: data block + the runtime module -->
```

The generator reads the method signature, `[Description]`, `[Range]`, `[StringLength]`, `[Required]` and the XML
summary, and emits the JSON Schema and a typed invoker at build time. There is no reflection over tool methods at
runtime and the server validates every call against the same schema.

## Safe by default

- A write needs `Consequential = true` and an explicit `Confirm`, or the build fails (WM0001).
- `Confirm.Page` shows a modal dialog that only a trusted click can allow, and the server refuses the call without a
  one-time token minted after that click. A script that calls the endpoint directly gets 403.
- Antiforgery, `[Authorize]` policies, a signed-in user for writes, a per-user rate limit, JSON-only requests and
  `Sec-Fetch-Site` checks sit in front of every call.

Details and limits (XSS is out of scope): [docs/security.md](docs/security.md). Diagnostics and their mapping to
Levelset checks: [docs/analyzers.md](docs/analyzers.md). A runnable app: [samples/NotesApp](samples/NotesApp).

## Packages

| Package | Contents |
|---|---|
| `Handrail` | `[WebMcpTool]`, descriptor model, confirmation tokens, source generator, analyzers |
| `Handrail.AspNetCore` | endpoints, `<webmcp-tools>` and form tag helpers, JavaScript runtime |

Not yet: Blazor components, an MCP bridge (`[McpExpose]`), a paid-tool bridge (`[PaidTool]` via x402-mcp-dotnet),
minimal-API `WithWebMcpTool`, a testing package.

Licence: MIT.
