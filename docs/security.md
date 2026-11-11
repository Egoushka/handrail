# Security model

A WebMCP tool runs in the user's tab with the user's cookies. Whoever can call `executeTool`, an agent or a script
injected into the page, acts as the user. Handrail narrows that; it does not remove it.

## What is enforced

1. **Writes need confirmation.** A tool without `ReadOnly = true` is a write. With `Confirm.Page` (the default), the
   runtime shows a modal `<dialog>` with the tool name and the arguments as text. Only a trusted click
   (`event.isTrusted`) allows; a script-made `click()` is ignored. Esc or Cancel declines. Dialogs queue, one at a time.
2. **The server re-checks.** `POST /_webmcp/invoke/{tool}` refuses a write without a one-time token. The dialog
   mints it through `POST /_webmcp/confirm/{tool}` after Allow. The token is an HMAC over tool, SHA-256 of the
   canonical arguments, user id and expiry (60 s), plus a nonce spent on first use. Replay, a different tool, a
   different argument, a different user and expiry all fail with 403.
3. **Same checks as the rest of the app.** Content type must be JSON (a cross-site HTML form cannot call it),
   `Sec-Fetch-Site` must be same-origin when sent, the antiforgery token must be valid, `[Authorize]` on the method or
   class is evaluated with the real policy provider, and a write needs a signed-in user unless
   `RequireAuthenticatedForWrites` is turned off. Inputs are validated against the schema; extra members are
   refused. Rate limit: 30 calls per minute per user and tool. Errors are short codes, never stack traces.
4. **Origin exposure.** Tools register with no `exposedTo` unless the page configures explicit HTTPS origins.
5. **Untrusted output.** `ReturnsUntrusted = true` sends `untrustedContentHint` and wraps the result in
   `<untrusted-content>` with control characters removed and the closing tag defused.
6. **No secrets in descriptors, no inline script.** WM0030 warns on secret-looking inputs. Descriptors live in a JSON
   data block that browsers do not execute; the runtime is a same-origin module, so a strict CSP needs no `unsafe-inline`.
   The data block encodes `<`, `>` and `&`, so a description cannot close the script element.

## What it does not stop

- **Script injection into your page (XSS).** A script on the page can read the antiforgery token and call
  `/confirm` and then `/invoke` itself. The token proves the call went through the confirm step, not that a person saw
  a dialog. Keep XSS out with a strict CSP and output encoding; treat Handrail as a second wall.
- **A user who clicks Allow without reading.** The dialog shows exactly what will run, in plain text.
- **Multi-instance deployments without a shared key.** Tokens and spent nonces live in process memory. Set
  `WebMcpOptions.ConfirmationKey` to the same bytes on every instance and keep a user on one instance (sticky routing),
  or a valid token can be refused (false negative) but never forged.
- **Browser behaviour.** The page cannot tell whether a browser honours `consequentialHint`. That is why
  `Confirm.Browser` is off by default.

Report vulnerabilities privately through GitHub security advisories on the repository.
