using System.Net;
using System.Text;
using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

// No test here ever contacts the real GitHub network — every call goes
// through FakeHttpMessageHandler.
public class GitHubApiClientTests
{
    [Fact]
    public async Task GetCurrentUserAsync_SendsExpectedRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"login":"octocat"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("https://api.github.com/user", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", request.Headers.Authorization.Parameter);
        Assert.Equal("RepoPulse", request.Headers.UserAgent.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
    }

    [Fact]
    public async Task GetCurrentUserAsync_SuccessResponse_ReturnsLoginAndAvatar()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"login":"octocat","avatar_url":"https://example.com/a.png"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("octocat", result.User!.Login);
        Assert.Equal("https://example.com/a.png", result.User.AvatarUrl);
    }

    [Fact]
    public async Task GetCurrentUserAsync_NonSuccessStatus_ReturnsFailureWithoutLeakingToken()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

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
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Timeout_ReturnsFailureSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("simulated timeout"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("test-access-token", result.SafeErrorMessage);
    }
}
