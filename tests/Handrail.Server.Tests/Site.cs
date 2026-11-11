using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Handrail;
using Handrail.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NotesApp;

namespace Handrail.Server.Tests;

public class ExtraTools
{
    [WebMcpTool("boom", ReadOnly = true, Description = "Always fails.")]
    public string Boom() => throw new InvalidOperationException("Server=db.internal;Password=hunter2");

    [Authorize(Roles = "admin")]
    [WebMcpTool("admin_report", ReadOnly = true, Description = "Admin only.")]
    public string Report() => "report";

    [WebMcpTool("ping", ReadOnly = true, Description = "Answers pong.")]
    public string Ping() => "pong";
}

/// <summary>The sample app in memory, with a cookie-keeping client, plus helpers that do what the runtime does.</summary>
internal sealed class Site : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public Site(Action<WebMcpOptions>? options = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddWebMcpTools<ExtraTools>();
            if (options is not null)
            {
                s.Configure(options);
            }
        }));
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public HttpClient Client { get; }

    public INoteStore Notes => _factory.Services.GetRequiredService<INoteStore>();

    public Task SignIn() => Client.PostAsync("/login", null);

    /// <summary>Loads the home page, as a browser would, and returns the data block the runtime reads.</summary>
    public async Task<JsonElement> Config()
    {
        var html = await Client.GetStringAsync("/");
        var match = Regex.Match(html, "<script type=\"application/json\" id=\"handrail-config\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success, html);
        return JsonDocument.Parse(match.Groups[1].Value).RootElement.Clone();
    }

    public async Task<HttpResponseMessage> Post(string path, object body, bool csrf = true, Action<HttpRequestMessage>? tweak = null)
    {
        var config = await Config();
        var request = new HttpRequestMessage(HttpMethod.Post, config.GetProperty("endpoint").GetString() + path) { Content = JsonContent.Create(body) };
        if (csrf)
        {
            request.Headers.Add(config.GetProperty("csrf").GetProperty("header").GetString()!, config.GetProperty("csrf").GetProperty("token").GetString());
        }

        tweak?.Invoke(request);
        return await Client.SendAsync(request);
    }

    public async Task<string> MintToken(string tool, object arguments)
    {
        var response = await Post("/confirm/" + tool, new { arguments });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}
