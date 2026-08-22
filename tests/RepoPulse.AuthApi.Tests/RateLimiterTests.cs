using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoPulse.AuthApi.GitHub;

namespace RepoPulse.AuthApi.Tests;

public class RateLimiterTests
{
    private const string EndpointPath = "/oauth/github/exchange";

    // Matches the PermitLimit configured for the "oauth-exchange" policy in
    // Program.cs. If that limit changes, this test's expectations must too.
    private const int PermitLimit = 10;

    [Fact]
    public async Task ExceedingPermitLimit_Returns429WithoutLeakingTokenOrCodeInfo()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(_ =>
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("""{"access_token":"upstream-token","token_type":"bearer"}""",
                                System.Text.Encoding.UTF8, "application/json")
                        }));
            });
        });
        using var client = factory.CreateClient();

        HttpResponseMessage? rejected = null;
        for (var i = 0; i < PermitLimit + 1; i++)
        {
            var response = await client.PostAsJsonAsync(EndpointPath, new { code = $"code-{i}", codeVerifier = TestConfiguration.ValidVerifier });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        Assert.NotNull(rejected);
        var body = await rejected!.Content.ReadAsStringAsync();
        Assert.Contains("rate_limited", body);
        Assert.DoesNotContain("code-", body);
        Assert.DoesNotContain("upstream-token", body);
    }

    [Fact]
    public async Task HealthEndpoint_IsNotRateLimited()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < PermitLimit + 5; i++)
        {
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
