using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoPulse.AuthApi.GitHub;

namespace RepoPulse.AuthApi.Tests;

// No test in this file (or the rest of the project) ever contacts the real
// GitHub network — every GitHub call is intercepted by FakeHttpMessageHandler
// or ThrowingHttpMessageHandler via ConfigurePrimaryHttpMessageHandler below.
public class GitHubTokenExchangeEndpointTests
{
    private const string EndpointPath = "/oauth/github/exchange";

    private static (WebApplicationFactory<Program> Factory, FakeHttpMessageHandler Handler) CreateFactoryWithFakeGitHub(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

        return (factory, handler);
    }

    private static WebApplicationFactory<Program> CreateFactoryWithThrowingGitHub(Exception exception)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHttpMessageHandler(exception));
            });
        });
    }

    private static HttpResponseMessage GitHubJson(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    // --- Outbound request shape ---------------------------------------

    [Fact]
    public async Task ValidRequest_ProducesExpectedGitHubFormFields()
    {
        var (factory, handler) = CreateFactoryWithFakeGitHub(_ =>
            GitHubJson(HttpStatusCode.OK, """{"access_token":"upstream-token","token_type":"bearer"}"""));
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });

        var body = handler.LastRequestBody!;
        Assert.Contains("client_id=test-client-id", body);
        Assert.Contains("code=abc123", body);
        Assert.Contains($"code_verifier={TestConfiguration.ValidVerifier}", body);
        Assert.Contains("redirect_uri=repopulse", body);
    }

    [Fact]
    public async Task ValidRequest_SendsClientSecretOnlyFromTestConfiguration()
    {
        var (factory, handler) = CreateFactoryWithFakeGitHub(_ =>
            GitHubJson(HttpStatusCode.OK, """{"access_token":"upstream-token","token_type":"bearer"}"""));
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });

        Assert.Contains("client_secret=test-client-secret", handler.LastRequestBody);
    }

    [Fact]
    public async Task Request_CannotOverrideClientIdRedirectUriOrTokenEndpoint()
    {
        // GitHubTokenExchangeRequest has no properties for these — with
        // UnmappedMemberHandling.Disallow, supplying them is a 400, not a
        // silently-ignored override.
        var (factory, _) = CreateFactoryWithFakeGitHub(_ =>
            GitHubJson(HttpStatusCode.OK, """{"access_token":"upstream-token","token_type":"bearer"}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new
        {
            code = "abc123",
            codeVerifier = TestConfiguration.ValidVerifier,
            clientId = "attacker-client-id",
            redirectUri = "https://evil.example.com/callback",
            tokenEndpoint = "https://evil.example.com/token"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Request_UnknownField_IsRejected()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ =>
            GitHubJson(HttpStatusCode.OK, """{"access_token":"upstream-token","token_type":"bearer"}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new
        {
            code = "abc123",
            codeVerifier = TestConfiguration.ValidVerifier,
            foo = "bar"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Request validation ---------------------------------------------

    [Theory]
    [InlineData("")]
    public async Task EmptyCode_IsRejected(string emptyCode)
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = emptyCode, codeVerifier = TestConfiguration.ValidVerifier });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TooLongCode_IsRejected()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new
        {
            code = new string('c', 513),
            codeVerifier = TestConfiguration.ValidVerifier
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(42)]  // one short of the RFC 7636 minimum
    [InlineData(129)] // one over the RFC 7636 maximum
    public async Task VerifierWithInvalidLength_IsRejected(int length)
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = new string('A', length) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("has a space in it and is long enough to otherwise pass 43chars!")]
    [InlineData("has+a+plus+sign+which+is+not+allowed+by+rfc7636+at+all!!")]
    public async Task VerifierWithInvalidCharacters_IsRejected(string invalidVerifier)
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = invalidVerifier });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Successful response mapping ------------------------------------

    [Fact]
    public async Task SuccessfulTokenResponse_MapsToSafeModel()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK,
            """{"access_token":"upstream-token","token_type":"bearer","scope":"read:user"}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("upstream-token", json.GetProperty("accessToken").GetString());
        Assert.Equal("bearer", json.GetProperty("tokenType").GetString());
        Assert.Equal("read:user", json.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task SuccessfulResponse_WithExpiryAndRefreshFields_MapsThemCorrectly()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK,
            """{"access_token":"upstream-token","token_type":"bearer","expires_in":28800,"refresh_token":"upstream-refresh","refresh_token_expires_in":15811200}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(28800, json.GetProperty("expiresIn").GetInt32());
        Assert.Equal("upstream-refresh", json.GetProperty("refreshToken").GetString());
        Assert.Equal(15811200L, json.GetProperty("refreshTokenExpiresIn").GetInt64());
    }

    [Fact]
    public async Task SuccessfulResponse_WithoutRefreshFields_IsStillSupported()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK,
            """{"access_token":"upstream-token","token_type":"bearer"}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("refreshToken").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("expiresIn").ValueKind);
    }

    // --- Failure mapping --------------------------------------------------

    [Fact]
    public async Task GitHubOAuthError_IsSanitizedBeforeReachingClient()
    {
        const string rawDescription = "the verification code has expired or is invalid - super secret internal detail";
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK,
            $$"""{"error":"bad_verification_code","error_description":"{{rawDescription}}"}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("oauth_exchange_failed", body);
        Assert.DoesNotContain(rawDescription, body);
        Assert.DoesNotContain("bad_verification_code", body);
    }

    [Fact]
    public async Task MalformedGitHubJson_ReturnsUpstreamError()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json {{{") });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("upstream_error", body);
    }

    [Fact]
    public async Task GitHubResponseMissingAccessTokenAndError_ReturnsUpstreamError()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("upstream_error", body);
    }

    [Fact]
    public async Task GitHubTimeout_ReturnsUpstreamTimeoutSafely()
    {
        var factory = CreateFactoryWithThrowingGitHub(new TaskCanceledException("simulated timeout"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("upstream_timeout", body);
        Assert.DoesNotContain("simulated timeout", body);
    }

    [Fact]
    public async Task GitHubNetworkFailure_ReturnsUpstreamErrorSafely()
    {
        var factory = CreateFactoryWithThrowingGitHub(new HttpRequestException("connection refused"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("upstream_error", body);
        Assert.DoesNotContain("connection refused", body);
    }

    // --- Response headers -------------------------------------------------

    [Fact]
    public async Task Response_IncludesNoStoreCacheHeaders()
    {
        var (factory, _) = CreateFactoryWithFakeGitHub(_ => GitHubJson(HttpStatusCode.OK,
            """{"access_token":"upstream-token","token_type":"bearer"}"""));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(EndpointPath, new { code = "abc123", codeVerifier = TestConfiguration.ValidVerifier });

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Contains("no-cache", string.Join(",", response.Headers.Pragma));
    }
}
