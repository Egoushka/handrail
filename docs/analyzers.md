# Analyzer and check IDs

Build-time diagnostics for tool authors. IDs mirror the checks of the companion site checker, Levelset, so a finding
on a live page and a build error in the source name the same problem. The mapping is a table here until both sides
exist; a shared catalog file is not created before then. Levelset IDs are from `research/specs/agent-ready/checker.md`.

| ID | Severity | Source | Levelset check | Meaning |
|---|---|---|---|---|
| WM0001 | error | analyzer, and registry at startup | WM-020 | A write (not `ReadOnly`) lacks `Consequential = true`, or has no explicit `Confirm`, or uses `Confirm.None` without `AllowUnconfirmedWrite`. |
| WM0002 | error | generator | WM-010 | Tool name is not 1-128 characters of `A-Za-z0-9_.-`. |
| WM0003 | error | generator | WM-031 | Two tools share a name. |
| WM0004 | error | generator | WM-011 | A parameter type has no JSON Schema mapping (`object`, `TimeSpan`, non-string dictionary keys, recursive types). |
| WM0005 | warning | analyzer | WM-022 | `ReadOnly = true` on a tool that is also `Consequential` or whose name reads like a write. |
| WM0006 | warning | generator | WM-012 | Tool has no description (no XML summary and no `Description`). |
| WM0007 | error | generator | WM-010 | Method is private, generic, or in a generic type. |
| WM0010 | warning | analyzer | WM-020 | `Confirm.None` with `AllowUnconfirmedWrite = true`: an agent or injected prompt can run the write as the user. |
| WM0011 | error at first render | form tag helper | WM-021 | `webmcp-autosubmit` on a form that is not `webmcp-readonly`. |
| WM0020 | warning | analyzer | WM-023 | Return type looks user-generated (Comment, Review, Message, Email, Feedback, Reply) without `ReturnsUntrusted = true`. |
| WM0030 | warning | analyzer | none yet | A parameter (or a property of a parameter record) is named like a secret: password, token, apiKey, secret, credential. |

Runtime checks without an analyzer: more than `MaxToolsPerPage` (40) tools on a page logs a warning (WM-030 in
Levelset); `WebMcpExposure.To(...)` throws on `*`, `http`, wildcards and paths (WM-032).

Suppress a diagnostic the normal way (`#pragma warning disable WM0010`, or `.editorconfig` severity). A suppressed
WM0001 still fails at startup: the registry applies the same rule.
