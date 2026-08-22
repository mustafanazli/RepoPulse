using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RepoPulse.AuthApi.Tests;

// End-to-end proof (not just the validator in isolation) that the host
// itself refuses to start when GitHub OAuth configuration — most
// importantly the secret — is missing. No real GitHub network call happens
// here; the host fails before any endpoint is reachable.
public class HostStartupValidationTests
{
    [Fact]
    public void Host_WithMissingClientSecret_FailsToStart()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitHubOAuth:ClientId"] = "test-client-id",
                    ["GitHubOAuth:ClientSecret"] = "",
                    ["GitHubOAuth:RedirectUri"] = "repopulse://oauth/callback",
                    ["GitHubOAuth:TokenEndpoint"] = "https://github.com/login/oauth/access_token"
                });
            });
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ClientSecret", exception.ToString());
    }

    [Fact]
    public void Host_WithAllRequiredConfiguration_StartsSuccessfully()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }
}
