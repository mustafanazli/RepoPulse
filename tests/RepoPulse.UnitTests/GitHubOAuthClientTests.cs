using System.Net;
using System.Net.Http;
using System.Text;
using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class GitHubOAuthClientTests
{
    [Fact]
    public async Task ExchangeCodeForTokenAsync_SuccessResponse_ReturnsAccessToken()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"access_token":"test-access-token","token_type":"bearer","scope":""}"""));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("test-access-token", result.Success!.AccessToken);
        Assert.Equal("bearer", result.Success.TokenType);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_DoesNotSendClientSecret()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"access_token":"test-access-token","token_type":"bearer"}"""));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        Assert.DoesNotContain("client_secret", handler.LastRequestBody);
        Assert.Contains("code_verifier=some-verifier", handler.LastRequestBody);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_OAuthErrorResponse_ReturnsFailureWithoutLeakingDescription()
    {
        const string secretLookingDescription = "the code abc-super-secret-code was already used";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            $$"""{"error":"bad_verification_code","error_description":"{{secretLookingDescription}}"}"""));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TokenExchangeFailureReason.OAuthError, result.FailureReason);
        Assert.Contains("bad_verification_code", result.SafeErrorMessage);
        Assert.DoesNotContain(secretLookingDescription, result.SafeErrorMessage);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_NonSuccessStatusWithoutBody_ReturnsFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TokenExchangeFailureReason.NonSuccessStatusCode, result.FailureReason);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_MalformedJson_ReturnsFailureAndDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all {{{", Encoding.UTF8, "application/json")
        });
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TokenExchangeFailureReason.MalformedResponse, result.FailureReason);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_NetworkFailure_ReturnsFailureAndDoesNotThrow()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TokenExchangeFailureReason.NetworkError, result.FailureReason);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_Timeout_ReturnsFailureAndDoesNotThrow()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("simulated timeout"));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.ExchangeCodeForTokenAsync("some-code", "some-verifier", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TokenExchangeFailureReason.Timeout, result.FailureReason);
    }

    [Fact]
    public async Task GetCurrentUserAsync_SuccessResponse_ReturnsLoginAndAvatar()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"login":"octocat","avatar_url":"https://example.com/avatar.png"}"""));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("octocat", result.User!.Login);
        Assert.Equal("https://example.com/avatar.png", result.User.AvatarUrl);
    }

    [Fact]
    public async Task GetCurrentUserAsync_SendsExpectedHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, """{"login":"octocat"}"""));
        var client = new GitHubOAuthClient(new HttpClient(handler));

        await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", request.Headers.Authorization.Parameter);
        Assert.Equal("RepoPulse", request.Headers.UserAgent.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
    }

    [Fact]
    public async Task GetCurrentUserAsync_NonSuccessStatus_ReturnsFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("test-access-token", result.SafeErrorMessage);
    }

    [Fact]
    public async Task GetCurrentUserAsync_MalformedJson_ReturnsFailureAndDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not valid", Encoding.UTF8, "application/json")
        });
        var client = new GitHubOAuthClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
