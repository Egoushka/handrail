using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Handrail.AspNetCore;

public static class WebMcpEndpoints
{
    /// <summary>Maps the runtime script (GET) and the confirm and invoke endpoints (POST only) under <see cref="WebMcpOptions.Path"/>.</summary>
    public static RouteGroupBuilder MapWebMcp(this IEndpointRouteBuilder app)
    {
        var path = app.ServiceProvider.GetRequiredService<IOptions<WebMcpOptions>>().Value.Path;
        _ = app.ServiceProvider.GetRequiredService<IWebMcpRegistry>(); // fail at startup, not on the first call
        var group = app.MapGroup(path).ExcludeFromDescription();

        group.MapGet("/runtime.js", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=3600";
            context.Response.Headers.ETag = $"\"{WebMcpRuntime.Version}\"";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.Text(WebMcpRuntime.Source, "text/javascript; charset=utf-8");
        });

        group.MapPost("/invoke/{tool}", (HttpContext context, string tool, WebMcpExecutor executor) => executor.HandleInvoke(context, tool));
        group.MapPost("/confirm/{tool}", (HttpContext context, string tool, WebMcpExecutor executor) => executor.HandleConfirm(context, tool));
        return group;
    }
}

internal static class WebMcpRuntime
{
    public static string Source { get; } = Load();

    public static string Version { get; } = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Source)))[..12].ToLowerInvariant();

    private static string Load()
    {
        using var stream = typeof(WebMcpRuntime).Assembly.GetManifestResourceStream("webmcp.runtime.js")
            ?? throw new InvalidOperationException("The embedded runtime is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
