using Microsoft.Extensions.Options;
using RepoPulse.AuthApi.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<GitHubOAuthOptions>()
    .Bind(builder.Configuration.GetSection(GitHubOAuthOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<GitHubOAuthOptions>, GitHubOAuthOptionsValidator>();

var app = builder.Build();

// Deliberately returns nothing beyond a fixed status literal — no
// configuration, options, or secret value is ever included in the response.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Exposed for WebApplicationFactory<Program> in RepoPulse.AuthApi.Tests.
public partial class Program { }
