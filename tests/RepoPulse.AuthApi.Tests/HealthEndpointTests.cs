using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RepoPulse.AuthApi.Tests;

// Uses only fake, clearly-not-real configuration values — never contacts GitHub.
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitHubOAuth:ClientId"] = "test-client-id",
                    ["GitHubOAuth:ClientSecret"] = "test-client-secret",
                    ["GitHubOAuth:RedirectUri"] = "repopulse://oauth/callback",
                    ["GitHubOAuth:TokenEndpoint"] = "https://github.com/login/oauth/access_token"
                });
            });
        });
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ResponseContainsNoConfigurationOrSecretValues()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.Equal("healthy", document.RootElement.GetProperty("status").GetString());

        Assert.DoesNotContain("test-client-secret", body);
        Assert.DoesNotContain("test-client-id", body);
        Assert.DoesNotContain("ClientSecret", body, StringComparison.OrdinalIgnoreCase);
    }
}
