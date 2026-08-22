using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoPulse.AuthApi.GitHub;

namespace RepoPulse.AuthApi.Tests;

public class RequestBodySizeTests
{
    private const string EndpointPath = "/oauth/github/exchange";

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
    }

    [Fact]
    public async Task OversizedRequestBody_IsRejectedWith413BeforeReachingValidation()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Comfortably over the 4096-byte cap, and also over the 512-char code
        // limit — the point is the *body-size* guard fires (413), not the
        // ordinary per-field validator (400).
        var oversizedCode = new string('c', 6000);

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = oversizedCode, codeVerifier = TestConfiguration.ValidVerifier });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task NormalSizedRequest_IsNotRejectedByBodySizeLimit()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
