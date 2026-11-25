# Changelog

## 0.1.0

First preview, published to NuGet as `Handrail` and `Handrail.AspNetCore`.

- `[WebMcpTool]` turns a C# method into a WebMCP tool; a source generator emits the JSON Schema and a typed invoker.
- Writes need `Consequential = true` and an explicit `Confirm`, or the build fails (WM0001).
- `Confirm.Page` modal with one-time confirmation tokens; antiforgery, `[Authorize]`, per-user rate limit and
  `Sec-Fetch-Site` checks in front of every call.
- ASP.NET Core endpoints, `<webmcp-tools>` and form tag helpers, and the browser runtime.
- Targets net8.0 and net10.0. WebMCP is a draft spec; expect changes.
