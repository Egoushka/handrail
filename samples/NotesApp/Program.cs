using System.Security.Claims;
using Handrail.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NotesApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddSingleton<INoteStore, InMemoryNoteStore>();
builder.Services.AddWebMcp(o =>
{
    o.ExposedTo = WebMcpExposure.SameOriginOnly;
    o.RequireAuthenticatedForWrites = true;
});
builder.Services.AddWebMcpTools<NotesTools>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapWebMcp();

// A stand-in for a real sign-in: the sample has one demo user.
app.MapPost("/login", async (HttpContext http) =>
{
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "demo"), new Claim(ClaimTypes.Name, "Demo")], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync();
    return Results.Redirect("/");
}).DisableAntiforgery();

app.Run();

public partial class Program;
