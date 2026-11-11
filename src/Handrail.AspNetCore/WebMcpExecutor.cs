using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Handrail.AspNetCore;

/// <summary>
/// The server half of every tool call. A request passes the checks in a fixed order, and each refusal is a short
/// error code with no detail an attacker could use: content type, fetch metadata, antiforgery, tool lookup,
/// authorization, rate limit, confirmation token, input validation.
/// </summary>
internal sealed class WebMcpExecutor(
    IWebMcpRegistry registry,
    ConfirmationTokenService tokens,
    WebMcpRateLimiter limiter,
    IAntiforgery antiforgery,
    IAuthorizationPolicyProvider policyProvider,
    IPolicyEvaluator policyEvaluator,
    IOptions<WebMcpOptions> options,
    ILogger<WebMcpExecutor> logger)
{
    private const int MaxBodyBytes = 64 * 1024;

    public Task HandleInvoke(HttpContext context, string tool) => Handle(context, tool, mint: false);

    public Task HandleConfirm(HttpContext context, string tool) => Handle(context, tool, mint: true);

    private async Task Handle(HttpContext context, string toolName, bool mint)
    {
        var clock = Stopwatch.StartNew();
        string outcome;
        string? user = null;
        try
        {
            (outcome, user) = await Run(context, toolName, mint);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = "cancelled";
        }

        logger.LogInformation("webmcp tool={Tool} action={Action} user={User} outcome={Outcome} ms={Elapsed}",
            toolName, mint ? "confirm" : "invoke", user is null ? "-" : Hash(user), outcome, clock.ElapsedMilliseconds);
    }

    private async Task<(string Outcome, string? User)> Run(HttpContext context, string toolName, bool mint)
    {
        var opts = options.Value;
        context.Response.Headers.CacheControl = "no-store";

        if (!context.Request.HasJsonContentType())
        {
            return (await Fail(context, 415, "unsupported_media_type", "Send application/json."), null);
        }

        // Fetch metadata: a browser tells us when a request comes from another site. Absent means not a browser fetch.
        if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var site) && site != "same-origin")
        {
            return (await Fail(context, 403, "forbidden_origin", "Cross-site requests are refused."), null);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return (await Fail(context, 403, "antiforgery", "Antiforgery validation failed."), null);
        }

        if (!registry.TryGet(toolName, out var tool))
        {
            return (await Fail(context, 404, "unknown_tool", "No such tool."), null);
        }

        var authorization = await Authorize(context, tool, opts);
        if (authorization is not null)
        {
            return (await Fail(context, authorization.Value, authorization.Value == 401 ? "unauthenticated" : "forbidden", "Not allowed."), null);
        }

        var user = UserKey(context);
        if (!limiter.TryAcquire(tool.Name + "|" + user))
        {
            context.Response.Headers.RetryAfter = "60";
            return (await Fail(context, 429, "rate_limited", "Too many calls; try again later."), user);
        }

        var body = await ReadBody(context);
        if (body is null)
        {
            return (await Fail(context, 400, "bad_request", "The body must be a JSON object under 64 KB."), user);
        }

        using var document = body;
        var root = document.RootElement;
        var arguments = root.TryGetProperty("arguments", out var a) ? a : JsonDocument.Parse("{}").RootElement;
        var hash = ConfirmationTokenService.HashArguments(arguments);

        if (mint)
        {
            if (!tool.IsWrite || tool.Confirm != Confirm.Page)
            {
                return (await Fail(context, 400, "bad_request", "This tool needs no confirmation token."), user);
            }

            var token = tokens.Mint(tool.Name, hash, user);
            return (await Write(context, 200, w =>
            {
                w.WriteBoolean("ok", true);
                w.WriteString("token", token);
            }, "token_issued"), user);
        }

        if (tool.IsWrite && tool.Confirm == Confirm.Page)
        {
            var supplied = root.TryGetProperty("confirmation", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var verdict = tokens.TryConsume(supplied, tool.Name, hash, user);
            if (verdict != ConfirmationFailure.None)
            {
                var code = verdict == ConfirmationFailure.Missing ? "confirmation_required" : "confirmation_invalid";
                return (await Fail(context, 403, code, "This action needs the user's confirmation."), user);
            }
        }

        try
        {
            var json = await tool.Invoke(context.RequestServices, arguments, context.RequestAborted);
            var result = tool.ReturnsUntrusted ? WebMcpUntrusted.Wrap(json) : json;
            return (await Write(context, 200, w =>
            {
                w.WriteBoolean("ok", true);
                w.WriteString("result", result);
            }, "ok"), user);
        }
        catch (WebMcpInputException e)
        {
            return (await Fail(context, 400, "invalid_input", e.Message), user);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The exception goes to the log, never to the caller: stack traces and messages can leak internals.
            logger.LogError(e, "webmcp tool {Tool} failed", tool.Name);
            return (await Fail(context, 500, "tool_failed", "The tool failed."), user);
        }
    }

    // Returns the status to refuse with, or null when the caller may proceed.
    private async Task<int?> Authorize(HttpContext context, WebMcpToolDefinition tool, WebMcpOptions opts)
    {
        var data = tool.Authorize
            .Select(r => (IAuthorizeData)new AuthorizeAttribute { Policy = r.Policy, Roles = r.Roles, AuthenticationSchemes = r.AuthenticationSchemes })
            .ToList();

        var mustBeSignedIn = tool.IsWrite && opts.RequireAuthenticatedForWrites;
        AuthorizationPolicy? policy = null;
        if (data.Count > 0 && !(tool.AllowAnonymous && !mustBeSignedIn))
        {
            policy = await AuthorizationPolicy.CombineAsync(policyProvider, data);
        }

        if (mustBeSignedIn)
        {
            var signedIn = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            policy = policy is null ? signedIn : AuthorizationPolicy.Combine(policy, signedIn);
        }

        if (policy is null)
        {
            return null;
        }

        var authenticated = await policyEvaluator.AuthenticateAsync(policy, context);
        var result = await policyEvaluator.AuthorizeAsync(policy, authenticated, context, null);
        if (result.Succeeded)
        {
            return null;
        }

        return result.Challenged ? 401 : 403;
    }

    private static string UserKey(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity.Name ?? "authenticated";
        }

        return "anon:" + context.Connection.RemoteIpAddress;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();

    private static async Task<JsonDocument?> ReadBody(HttpContext context)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
            {
                return null;
            }
        }

        if (buffer.Length == 0)
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && (!document.RootElement.TryGetProperty("arguments", out var args) || args.ValueKind == JsonValueKind.Object))
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task<string> Fail(HttpContext context, int status, string code, string message) =>
        Write(context, status, w =>
        {
            w.WriteBoolean("ok", false);
            w.WriteStartObject("error");
            w.WriteString("code", code);
            w.WriteString("message", message);
            w.WriteEndObject();
        }, code);

    private static async Task<string> Write(HttpContext context, int status, Action<Utf8JsonWriter> body, string outcome)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.Body.WriteAsync(stream.ToArray(), context.RequestAborted);
        return outcome;
    }
}
