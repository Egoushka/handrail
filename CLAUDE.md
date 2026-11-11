# Handrail

WebMCP library for ASP.NET Core and Blazor. Spec: `research/specs/agent-ready/webmcp-dotnet.md` and `connect.md`
(Egoushka/research). Companion checker: Levelset; analyzer IDs mirror its check IDs (`docs/analyzers.md`).

## Commands

- Build and test: `dotnet test Handrail.slnx`. JS runtime tests: `node --test src/Handrail.AspNetCore/runtime/webmcp.runtime.test.mjs`.
- Box memory is tight: `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false`, and
  kill leftover `dotnet` processes afterwards. CI is the real gate.
- Warnings are errors. Public API needs no XML docs (CS1591 is off) but say why in comments, not what.

## Rules

- WebMCP facts come from the spec, not memory: `document.modelContext` (fallback `navigator.modelContext` with a
  console warning), `registerTool(tool, { signal, exposedTo })`, unregister by aborting the signal, annotations
  `readOnlyHint`, `untrustedContentHint`, `consequentialHint`. Never emit `unregisterTool` or `provideContext`.
- A security default needs a test that fails without it.
- No time or effort estimates in docs.
- One task, one branch, one PR; squash-merge; Conventional Commit titles.
- Do not publish to NuGet; that is the owner's action.
