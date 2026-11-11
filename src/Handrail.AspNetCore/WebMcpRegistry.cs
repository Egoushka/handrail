using System.Reflection;
using Microsoft.Extensions.Options;

namespace Handrail.AspNetCore;

/// <summary>Tools registered for one set. Several sources can exist; the registry merges and validates them.</summary>
internal sealed record WebMcpToolSource(IReadOnlyList<WebMcpToolDefinition> Tools);

public interface IWebMcpRegistry
{
    IReadOnlyList<WebMcpToolDefinition> Tools { get; }

    bool TryGet(string name, out WebMcpToolDefinition tool);
}

internal sealed class WebMcpRegistry : IWebMcpRegistry
{
    private readonly Dictionary<string, WebMcpToolDefinition> _byName = new(StringComparer.Ordinal);

    public WebMcpRegistry(IEnumerable<WebMcpToolSource> sources, IOptions<WebMcpOptions> options)
    {
        foreach (var tool in sources.SelectMany(s => s.Tools))
        {
            Validate(tool, options.Value);
            if (!_byName.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException($"WM0003: tool name '{tool.Name}' is registered twice.");
            }
        }

        Tools = _byName.Values.ToList();
    }

    public IReadOnlyList<WebMcpToolDefinition> Tools { get; }

    public bool TryGet(string name, out WebMcpToolDefinition tool) => _byName.TryGetValue(name, out tool!);

    // The same rules the analyzers enforce at build time, so a tool set built without them cannot slip an unconfirmed write through.
    private static void Validate(WebMcpToolDefinition tool, WebMcpOptions options)
    {
        if (!tool.IsWrite)
        {
            return;
        }

        if (tool.Confirm == Confirm.None && !tool.AllowUnconfirmedWrite)
        {
            throw new InvalidOperationException($"WM0001: write tool '{tool.Name}' uses Confirm.None without AllowUnconfirmedWrite = true.");
        }

        if (tool.Confirm == Confirm.Browser && !options.AllowBrowserConfirm)
        {
            throw new InvalidOperationException($"WM0001: write tool '{tool.Name}' uses Confirm.Browser, which is off. Set WebMcpOptions.AllowBrowserConfirm to allow it.");
        }

        if (tool.Confirm != Confirm.None && !tool.Consequential)
        {
            throw new InvalidOperationException($"WM0001: write tool '{tool.Name}' must set Consequential = true.");
        }
    }

    public static IReadOnlyList<WebMcpToolDefinition> FromAssembly(Assembly assembly) =>
        assembly.GetCustomAttributes<WebMcpToolSetAttribute>()
            .SelectMany(a => ((IWebMcpToolSet)Activator.CreateInstance(a.ToolSetType)!).Tools)
            .ToList();
}
