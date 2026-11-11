using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Handrail.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Handrail.Tests;

/// <summary>Compiles C# text with the Handrail generator and analyzers, in memory.</summary>
internal static class Compile
{
    private static readonly MetadataReference[] References = BuildReferences();

    private static MetadataReference[] BuildReferences()
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var refs = trusted
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(WebMcpToolAttribute).Assembly.Location));
        return [.. refs];
    }

    public static CSharpCompilation Create(string source, string assemblyName = "TestApp") =>
        CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public sealed record Result(Compilation Output, ImmutableArray<Diagnostic> Diagnostics, string Generated);

    public static Result Generate(string source, string assemblyName = "TestApp")
    {
        var driver = CSharpGeneratorDriver.Create(new WebMcpToolGenerator()).RunGeneratorsAndUpdateCompilation(
            Create(source, assemblyName), out var output, out var diagnostics);
        var generated = driver.GetRunResult().GeneratedTrees.FirstOrDefault()?.ToString() ?? string.Empty;
        return new Result(output, diagnostics, generated);
    }

    public static IReadOnlyList<WebMcpToolDefinition> Tools(string source)
    {
        var result = Generate(source, "TestApp" + Guid.NewGuid().ToString("N"));
        var errors = result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + result.Generated);

        using var stream = new MemoryStream();
        var emit = result.Output.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(stream.ToArray()));
        return assembly.GetCustomAttributes<WebMcpToolSetAttribute>()
            .SelectMany(a => ((IWebMcpToolSet)Activator.CreateInstance(a.ToolSetType)!).Tools)
            .ToList();
    }

    public static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var compilation = Create(source);
        var withAnalyzers = compilation.WithAnalyzers([new Handrail.Analyzers.WebMcpToolAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
