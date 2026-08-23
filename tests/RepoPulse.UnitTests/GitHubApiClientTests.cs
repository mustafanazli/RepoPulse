using System.Net;
using System.Text;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;

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

    [Fact]
    public async Task GetCurrentUserAsync_WebException_ReturnsFailureSafely()
    {
        // Reproduces a real crash found via Android emulator testing (RP-006
        // verification): Xamarin.Android's HTTP handler can surface a raw
        // socket failure as WebException instead of HttpRequestException.
        var handler = new ThrowingHttpMessageHandler(new WebException("Socket closed"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetCurrentUserAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("test-access-token", result.SafeErrorMessage);
    }

    // --- GetRepositoryAsync (RP-006) ---
    // No test here ever contacts the real GitHub network — every call goes
    // through FakeHttpMessageHandler/ThrowingHttpMessageHandler.

    private const string FullRepositoryJson = """
        {
          "name": "RepoPulse",
          "full_name": "mustafanazli/RepoPulse",
          "owner": { "login": "mustafanazli" },
          "description": "A repository health dashboard",
          "html_url": "https://github.com/mustafanazli/RepoPulse",
          "stargazers_count": 42,
          "forks_count": 7,
          "open_issues_count": 3,
          "language": "C#",
          "default_branch": "main",
          "archived": false,
          "fork": false,
          "updated_at": "2026-01-15T10:30:00Z",
          "pushed_at": "2026-01-14T08:00:00Z"
        }
        """;

    [Fact]
    public async Task GetRepositoryAsync_SendsExpectedRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FullRepositoryJson, Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("https://api.github.com/repos/mustafanazli/RepoPulse", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", request.Headers.Authorization.Parameter);
        Assert.Equal("RepoPulse", request.Headers.UserAgent.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
    }

    [Fact]
    public async Task GetRepositoryAsync_EncodesOwnerAndNameSegments()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FullRepositoryJson, Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetRepositoryAsync("test-access-token", "owner name", "repo/name", CancellationToken.None);

        var request = handler.LastRequest!;
        // AbsoluteUri (not ToString(), which unescapes "%20" back to a space
        // for display) reflects the exact escaped form actually sent on the
        // wire.
        Assert.Equal("https://api.github.com/repos/owner%20name/repo%2Fname", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetRepositoryAsync_TokenNeverAppearsInRequestUriOrBody()
    {
        const string token = "SUPER-SECRET-TOKEN-abc123";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FullRepositoryJson, Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetRepositoryAsync(token, "mustafanazli", "RepoPulse", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.DoesNotContain(token, request.RequestUri!.ToString());
        Assert.Null(request.Content);
        // The token must only ever appear in the Authorization header value —
        // never anywhere else on the request.
        Assert.Equal(token, request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetRepositoryAsync_SuccessResponse_MapsAllFields()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(FullRepositoryJson, Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var repository = result.Repository!;
        Assert.Equal("mustafanazli", repository.Owner);
        Assert.Equal("RepoPulse", repository.Name);
        Assert.Equal("mustafanazli/RepoPulse", repository.FullName);
        Assert.Equal("A repository health dashboard", repository.Description);
        Assert.Equal("https://github.com/mustafanazli/RepoPulse", repository.HtmlUrl);
        Assert.Equal(42, repository.Stars);
        Assert.Equal(7, repository.Forks);
        Assert.Equal(3, repository.OpenIssuesAndPullRequests);
        Assert.Equal("C#", repository.PrimaryLanguage);
        Assert.Equal("main", repository.DefaultBranch);
        Assert.False(repository.IsArchived);
        Assert.False(repository.IsFork);
        Assert.Equal(DateTimeOffset.Parse("2026-01-15T10:30:00Z"), repository.UpdatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-01-14T08:00:00Z"), repository.PushedAt);
    }

    [Fact]
    public async Task GetRepositoryAsync_NullableDescriptionAndLanguage_MapToNull()
    {
        const string json = """
            {
              "name": "RepoPulse",
              "full_name": "mustafanazli/RepoPulse",
              "owner": { "login": "mustafanazli" },
              "description": null,
              "html_url": "https://github.com/mustafanazli/RepoPulse",
              "stargazers_count": 0,
              "forks_count": 0,
              "open_issues_count": 0,
              "language": null,
              "default_branch": "main",
              "archived": true,
              "fork": true
            }
            """;
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var repository = result.Repository!;
        Assert.Null(repository.Description);
        Assert.Null(repository.PrimaryLanguage);
        Assert.True(repository.IsArchived);
        Assert.True(repository.IsFork);
        Assert.Null(repository.UpdatedAt);
        Assert.Null(repository.PushedAt);
    }

    [Fact]
    public async Task GetRepositoryAsync_NotFound_ReturnsNotFoundFailureKind()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"message":"Not Found"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "does-not-exist", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task GetRepositoryAsync_Unauthorized_ReturnsUnauthorizedFailureKind()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unauthorized, result.FailureKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task GetRepositoryAsync_ForbiddenOrTooManyRequests_ReturnsRateLimitedFailureKind(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("""{"message":"API rate limit exceeded"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.RateLimited, result.FailureKind);
    }

    [Fact]
    public async Task GetRepositoryAsync_RateLimitResponse_DoesNotExposeRawHeadersOrBody()
    {
        const string marker = "RATE-LIMIT-BODY-MARKER-9f3a";
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent($$"""{"message":"{{marker}}"}""", Encoding.UTF8, "application/json")
            };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "1234567890");
            return response;
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.RateLimited, result.FailureKind);
        // The typed result carries no free-text field at all for failures —
        // only the enum — so there is structurally nowhere for the raw body
        // or rate-limit headers to leak into.
        Assert.Null(result.Repository);
    }

    [Fact]
    public async Task GetRepositoryAsync_MalformedJson_ReturnsUnexpectedFailureAndDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not valid", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unexpected, result.FailureKind);
    }

    [Fact]
    public async Task GetRepositoryAsync_MissingRequiredFields_ReturnsUnexpectedFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unexpected, result.FailureKind);
    }

    [Fact]
    public async Task GetRepositoryAsync_NetworkException_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NetworkError, result.FailureKind);
    }

    [Fact]
    public async Task GetRepositoryAsync_Timeout_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("simulated timeout"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NetworkError, result.FailureKind);
    }

    [Fact]
    public async Task GetRepositoryAsync_WebException_ReturnsNetworkErrorFailureKindSafely()
    {
        // Reproduces a real crash found via Android emulator testing (RP-006
        // verification): Xamarin.Android's HTTP handler can surface a raw
        // socket failure as WebException instead of HttpRequestException.
        var handler = new ThrowingHttpMessageHandler(new WebException("Socket closed"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetRepositoryAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NetworkError, result.FailureKind);
    }
}
