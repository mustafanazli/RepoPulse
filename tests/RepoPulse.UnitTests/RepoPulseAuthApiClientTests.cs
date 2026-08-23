using System.Net;
using System.Reflection;
using System.Text;
using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

// No test here ever contacts the real RepoPulse.AuthApi backend or GitHub —
// every call goes through FakeHttpMessageHandler / ThrowingHttpMessageHandler.
public class RepoPulseAuthApiClientTests
{
    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ExchangeAsync_UsesExpectedRequestUri()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"a","tokenType":"bearer"}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        await client.ExchangeAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.Equal("https://localhost:7082/oauth/github/exchange", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ExchangeAsync_RequestBody_ContainsOnlyCodeAndCodeVerifier()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"a","tokenType":"bearer"}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        await client.ExchangeAsync("some-code", "some-verifier", CancellationToken.None);

        var body = handler.LastRequestBody!;
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var propertyNames = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "code", "codeVerifier" }, propertyNames);
        Assert.Equal("some-code", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("some-verifier", doc.RootElement.GetProperty("codeVerifier").GetString());
    }

    [Fact]
    public async Task ExchangeAsync_DoesNotSendClientIdSecretRedirectUriOrState()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"a","tokenType":"bearer"}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        await client.ExchangeAsync("some-code", "some-verifier", CancellationToken.None);

        var body = handler.LastRequestBody!;
        Assert.DoesNotContain("clientId", body);
        Assert.DoesNotContain("clientSecret", body);
        Assert.DoesNotContain("redirectUri", body);
        Assert.DoesNotContain("tokenEndpoint", body);
        Assert.DoesNotContain("state", body);
    }

    [Fact]
    public async Task ExchangeAsync_SuccessfulResponse_MapsToModel()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"upstream-token","tokenType":"bearer","scope":"read:user"}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("upstream-token", result.Success!.AccessToken);
        Assert.Equal("bearer", result.Success.TokenType);
        Assert.Equal("read:user", result.Success.Scope);
    }

    [Fact]
    public async Task ExchangeAsync_ResponseWithExpiryAndRefreshFields_MapsThemCorrectly()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"a","tokenType":"bearer","expiresIn":28800,"refreshToken":"r","refreshTokenExpiresIn":15811200}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.Equal(28800, result.Success!.ExpiresIn);
        Assert.Equal("r", result.Success.RefreshToken);
        Assert.Equal(15811200L, result.Success.RefreshTokenExpiresIn);
    }

    [Fact]
    public async Task ExchangeAsync_ResponseWithoutOptionalFields_IsStillSupported()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"accessToken":"a","tokenType":"bearer"}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Success!.ExpiresIn);
        Assert.Null(result.Success.RefreshToken);
        Assert.Null(result.Success.RefreshTokenExpiresIn);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request", AuthApiExchangeFailureKind.InvalidRequest)]
    [InlineData(HttpStatusCode.BadRequest, "oauth_exchange_failed", AuthApiExchangeFailureKind.OAuthExchangeFailed)]
    [InlineData(HttpStatusCode.BadGateway, "upstream_error", AuthApiExchangeFailureKind.UpstreamError)]
    [InlineData(HttpStatusCode.GatewayTimeout, "upstream_timeout", AuthApiExchangeFailureKind.UpstreamTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", AuthApiExchangeFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error", AuthApiExchangeFailureKind.InternalError)]
    public async Task ExchangeAsync_BackendErrorTitles_MapToExpectedFailureKind(HttpStatusCode statusCode, string title, AuthApiExchangeFailureKind expectedKind)
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(statusCode,
            $$"""{"type":"about:blank","title":"{{title}}","status":{{(int)statusCode}}}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedKind, result.FailureKind);
    }

    [Fact]
    public async Task ExchangeAsync_MalformedJson_ReturnsMalformedResponseAndDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json {{{") });
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthApiExchangeFailureKind.MalformedResponse, result.FailureKind);
    }

    [Fact]
    public async Task ExchangeAsync_MissingAccessToken_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthApiExchangeFailureKind.MalformedResponse, result.FailureKind);
    }

    [Fact]
    public async Task ExchangeAsync_Timeout_ReturnsTimeoutFailureSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("simulated timeout"));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthApiExchangeFailureKind.Timeout, result.FailureKind);
    }

    [Fact]
    public async Task ExchangeAsync_NetworkFailure_ReturnsNetworkErrorSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthApiExchangeFailureKind.NetworkError, result.FailureKind);
    }

    [Fact]
    public async Task ExchangeAsync_WebException_ReturnsNetworkErrorSafely()
    {
        // Reproduces a real crash found via Android emulator testing (RP-006
        // verification): Xamarin.Android's HTTP handler can surface a raw
        // socket failure as WebException instead of HttpRequestException.
        var handler = new ThrowingHttpMessageHandler(new WebException("Socket closed"));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthApiExchangeFailureKind.NetworkError, result.FailureKind);
    }

    [Fact]
    public async Task ExchangeAsync_DoesNotRetry_CallsHandlerExactlyOnce()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return JsonResponse(HttpStatusCode.BadGateway, """{"title":"upstream_error"}""");
        });
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExchangeAsync_FailureResult_NeverExposesRawResponseBody()
    {
        const string marker = "SUPER-SENSITIVE-MARKER-4f8a1c";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.BadRequest,
            $$"""{"title":"oauth_exchange_failed","detail":"{{marker}}"}"""));
        var client = new RepoPulseAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7082") });

        var result = await client.ExchangeAsync("code", "verifier", CancellationToken.None);

        // AuthApiExchangeResult structurally carries no free-text field for
        // failures — only the typed FailureKind enum — so there is nowhere
        // for a raw backend response to leak into. Reflection over every
        // public string-valued property proves the marker is absent.
        foreach (var prop in typeof(AuthApiExchangeResult).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(result);
            Assert.DoesNotContain(marker, value?.ToString() ?? string.Empty);
        }
    }
}
