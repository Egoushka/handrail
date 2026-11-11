using System.Reflection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Handrail.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>Adds the invoke pipeline: confirmation tokens, rate limiting, antiforgery and authorization.</summary>
    public static IServiceCollection AddWebMcp(this IServiceCollection services, Action<WebMcpOptions>? configure = null)
    {
        var options = services.AddOptions<WebMcpOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.AddAntiforgery();
        services.Configure<AntiforgeryOptions>(o => o.HeaderName ??= "X-CSRF-TOKEN");
        services.AddAuthorization();
        services.TryAddSingleton(sp => new ConfirmationTokenService(sp.GetRequiredService<IOptions<WebMcpOptions>>().Value.ConfirmationKey));
        services.TryAddSingleton<IWebMcpRegistry, WebMcpRegistry>();
        services.TryAddSingleton<WebMcpRateLimiter>();
        services.TryAddSingleton<WebMcpExecutor>();
        return services;
    }

    /// <summary>Registers the generated tools declared on <typeparamref name="T"/>, and <typeparamref name="T"/> itself as a scoped service.</summary>
    public static IServiceCollection AddWebMcpTools<T>(this IServiceCollection services)
        where T : class
    {
        var tools = WebMcpRegistry.FromAssembly(typeof(T).Assembly).Where(t => t.DeclaringType == typeof(T)).ToList();
        if (tools.Count == 0)
        {
            throw new InvalidOperationException($"{typeof(T).FullName} declares no [WebMcpTool] methods, or the Handrail source generator did not run for its assembly.");
        }

        services.TryAddScoped<T>();
        services.AddSingleton(new WebMcpToolSource(tools));
        return services;
    }

    /// <summary>Registers every tool of an assembly. Tool classes must be registered in the container by the caller.</summary>
    public static IServiceCollection AddWebMcpToolsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        var tools = WebMcpRegistry.FromAssembly(assembly);
        foreach (var type in tools.Select(t => t.DeclaringType).Distinct())
        {
            services.TryAddScoped(type);
        }

        services.AddSingleton(new WebMcpToolSource(tools));
        return services;
    }
}
