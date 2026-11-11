using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Handrail.Tests;

public class GeneratorTests
{
    private const string Notes = """
        using System.ComponentModel;
        using System.ComponentModel.DataAnnotations;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        using Handrail;

        namespace Demo;

        public enum Sort { Newest, Oldest }
        public record NoteSummary(System.Guid Id, string Title, string? Body);

        public class NotesTools
        {
            /// <summary>Search the user's notes by text. Use before <see cref="Get"/>.</summary>
            [WebMcpTool("search_notes", ReadOnly = true)]
            public Task<IReadOnlyList<NoteSummary>> Search(
                [Description("Words to look for")] string query,
                [Range(1, 20)] int limit = 5,
                Sort sort = Sort.Newest,
                CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<NoteSummary>>([new NoteSummary(System.Guid.Empty, query + limit + sort, null)]);

            [WebMcpTool(ReadOnly = true, Description = "Get one note.")]
            public NoteSummary Get(System.Guid id) => new(id, "t", null);

            [WebMcpTool(Consequential = true, Confirm = Confirm.Page, Description = "Delete a note.")]
            public void DeleteNote(System.Guid id) { }
        }
        """;

    [Fact]
    public void Input_schema_follows_the_signature()
    {
        var tool = Compile.Tools(Notes).Single(t => t.Name == "search_notes");

        Assert.Equal(
            """{"type":"object","properties":{"query":{"type":"string","description":"Words to look for"},"limit":{"type":"integer","minimum":1,"maximum":20,"default":5},"sort":{"type":"string","enum":["Newest","Oldest"],"default":"Newest"}},"required":["query"],"additionalProperties":false}""",
            tool.InputSchema);
        Assert.True(tool.ReadOnly);
    }

    [Fact]
    public void Description_comes_from_the_xml_summary_and_names_default_to_snake_case()
    {
        var tools = Compile.Tools(Notes);

        Assert.Equal("Search the user's notes by text. Use before Get.", tools.Single(t => t.Name == "search_notes").Description);
        Assert.Contains(tools, t => t.Name == "delete_note" && t.Title == "Delete note");
        Assert.Contains(tools, t => t.Name == "get" && t.Description == "Get one note.");
    }

    [Fact]
    public void Object_results_get_an_output_schema_and_lists_do_not()
    {
        var tools = Compile.Tools(Notes);

        Assert.Equal(
            """{"type":"object","properties":{"id":{"type":"string","format":"uuid"},"title":{"type":"string"},"body":{"type":"string"}},"required":["id","title"]}""",
            tools.Single(t => t.Name == "get").OutputSchema);
        Assert.Null(tools.Single(t => t.Name == "search_notes").OutputSchema);
    }

    [Fact]
    public async Task Invoker_binds_arguments_and_serializes_the_result()
    {
        var tool = Compile.Tools(Notes).Single(t => t.Name == "search_notes");
        var services = new Services(new Dictionary<Type, object> { [tool.DeclaringType] = Activator.CreateInstance(tool.DeclaringType)! });

        var json = await tool.Invoke(services, Parse("""{"query":"milk","limit":3}"""), CancellationToken.None);

        Assert.Equal("""[{"id":"00000000-0000-0000-0000-000000000000","title":"milk3Newest"}]""", json);
    }

    [Theory]
    [InlineData("""{}""", "'query' is required")]
    [InlineData("""{"query":"a","extra":1}""", "Unknown argument 'extra'")]
    [InlineData("""{"query":"a","limit":21}""", "between 1 and 20")]
    [InlineData("""{"query":"a","limit":"many"}""", "wrong type")]
    [InlineData("""{"query":null}""", "'query' is required")]
    [InlineData("""[1]""", "JSON object")]
    public async Task Invoker_rejects_bad_input(string args, string message)
    {
        var tool = Compile.Tools(Notes).Single(t => t.Name == "search_notes");
        var services = new Services(new Dictionary<Type, object> { [tool.DeclaringType] = Activator.CreateInstance(tool.DeclaringType)! });

        var error = await Assert.ThrowsAsync<WebMcpInputException>(async () => await tool.Invoke(services, Parse(args), CancellationToken.None));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task A_method_without_a_result_answers_ok()
    {
        var tool = Compile.Tools(Notes).Single(t => t.Name == "delete_note");
        var services = new Services(new Dictionary<Type, object> { [tool.DeclaringType] = Activator.CreateInstance(tool.DeclaringType)! });

        Assert.Equal(WebMcpJson.Ok, await tool.Invoke(services, Parse("""{"id":"00000000-0000-0000-0000-000000000000"}"""), CancellationToken.None));
    }

    [Fact]
    public void Generated_code_is_empty_when_there_are_no_tools()
    {
        Assert.Equal(string.Empty, Compile.Generate("class A {}").Generated);
    }

    [Theory]
    [InlineData("""[WebMcpTool("bad name", ReadOnly = true, Description = "x")] public void M() {}""", "WM0002")]
    [InlineData("""[WebMcpTool(ReadOnly = true)] public void M() {}""", "WM0006")]
    [InlineData("""[WebMcpTool(ReadOnly = true, Description = "x")] public void M(object o) {}""", "WM0004")]
    [InlineData("""[WebMcpTool(ReadOnly = true, Description = "x")] public void M(System.TimeSpan t) {}""", "WM0004")]
    [InlineData("""[WebMcpTool(ReadOnly = true, Description = "x")] public void M<T>(T t) {}""", "WM0007")]
    [InlineData("""[WebMcpTool(ReadOnly = true, Description = "x")] private void M() {}""", "WM0007")]
    public void Build_diagnostics(string member, string id)
    {
        var result = Compile.Generate($"using Handrail; public class T {{ {member} }}");

        Assert.Contains(result.Diagnostics, d => d.Id == id);
    }

    [Fact]
    public void Duplicate_names_are_an_error_and_only_the_first_is_emitted()
    {
        var result = Compile.Generate("""
            using Handrail;
            public class A { [WebMcpTool("same", ReadOnly = true, Description = "x")] public void M() {} }
            public class B { [WebMcpTool("same", ReadOnly = true, Description = "x")] public void M() {} }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "WM0003");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result.Generated, "Name = \"same\""));
    }

    [Fact]
    public void An_unrelated_edit_does_not_rerun_the_tool_pipeline()
    {
        var options = new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true);
        var first = Compile.Create(Notes);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new Generators.WebMcpToolGenerator().AsSourceGenerator()], driverOptions: options);
        driver = driver.RunGenerators(first);

        var second = first.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated { }"));
        driver = driver.RunGenerators(second);

        var steps = driver.GetRunResult().Results[0].TrackedSteps["tools"];
        Assert.All(steps.SelectMany(s => s.Outputs), o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class Services(Dictionary<Type, object> map) : IServiceProvider
    {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }
}
