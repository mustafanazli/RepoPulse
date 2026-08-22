using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoPulse.AuthApi.GitHub;

namespace RepoPulse.AuthApi.Tests;

public class LogSafetyTests
{
    private const string EndpointPath = "/oauth/github/exchange";

    // Distinctive marker values — if any of these ever show up in a captured
    // log line, something logged a raw OAuth secret.
    private const string SecretMarkerCode = "MARKER-CODE-7f3a9c2e";
    private const string SecretMarkerAccessToken = "MARKER-ACCESS-TOKEN-9b1d4e";
    private const string SecretMarkerRefreshToken = "MARKER-REFRESH-TOKEN-2c8f61";
    private static readonly string SecretMarkerVerifier = "MARKERVERIFIER" + new string('x', 43 - "MARKERVERIFIER".Length);

    [Fact]
    public async Task SuccessfulExchange_NeverLogsCodeVerifierTokenOrSecret()
    {
        var loggerProvider = new CapturingLoggerProvider();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(_ =>
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""{"access_token":"{{SecretMarkerAccessToken}}","token_type":"bearer","refresh_token":"{{SecretMarkerRefreshToken}}"}""",
                                System.Text.Encoding.UTF8, "application/json")
                        }));
            });
        });
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(EndpointPath, new { code = SecretMarkerCode, codeVerifier = SecretMarkerVerifier });

        AssertNoMarkerLeaked(loggerProvider);
    }

    [Fact]
    public async Task FailedExchange_NeverLogsCodeVerifierOrSecret()
    {
        var loggerProvider = new CapturingLoggerProvider();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHttpMessageHandler(new HttpRequestException("boom")));
            });
        });
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(EndpointPath, new { code = SecretMarkerCode, codeVerifier = SecretMarkerVerifier });

        AssertNoMarkerLeaked(loggerProvider);
    }

    private static void AssertNoMarkerLeaked(CapturingLoggerProvider loggerProvider)
    {
        var allMessages = string.Join("\n", loggerProvider.Messages);

        Assert.DoesNotContain(SecretMarkerCode, allMessages);
        Assert.DoesNotContain(SecretMarkerVerifier, allMessages);
        Assert.DoesNotContain(SecretMarkerAccessToken, allMessages);
        Assert.DoesNotContain(SecretMarkerRefreshToken, allMessages);
        Assert.DoesNotContain("test-client-secret", allMessages);
    }
}
