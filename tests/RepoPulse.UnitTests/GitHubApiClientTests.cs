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

    // --- GetUserRepositoriesAsync (RP-009) ---
    // No test here ever contacts the real GitHub network — every call goes
    // through FakeHttpMessageHandler/ThrowingHttpMessageHandler, and no real
    // GitHub token is ever used, only obviously-fake fixtures.

    private static string MinimalRepositoryJson(string fullName)
    {
        var (owner, name) = SplitFullName(fullName);
        return $$"""
            {
              "name": "{{name}}",
              "full_name": "{{fullName}}",
              "owner": { "login": "{{owner}}" },
              "html_url": "https://github.com/{{fullName}}",
              "default_branch": "main"
            }
            """;
    }

    private static string RepositoryJsonWithExplicitNulls(string fullName)
    {
        var (owner, name) = SplitFullName(fullName);
        return $$"""
            {
              "name": "{{name}}",
              "full_name": "{{fullName}}",
              "owner": { "login": "{{owner}}" },
              "description": null,
              "html_url": "https://github.com/{{fullName}}",
              "language": null,
              "default_branch": "main",
              "updated_at": null,
              "pushed_at": null
            }
            """;
    }

    private static (string Owner, string Name) SplitFullName(string fullName)
    {
        var segments = fullName.Split('/');
        return (segments[0], segments[1]);
    }

    private static string RepositoryArrayJson(params string[] repositoryJsonItems) =>
        "[" + string.Join(",", repositoryJsonItems) + "]";

    private static string NextLinkHeader(int page) =>
        $"<https://api.github.com/user/repos?page={page}&per_page=100>; rel=\"next\"";

    private static int GetRequestedPage(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            if (parts[0] == "page")
            {
                return int.Parse(parts[1]);
            }
        }

        return 1;
    }

    // 1. Empty success list.
    [Fact]
    public async Task GetUserRepositoriesAsync_EmptyResponse_ReturnsEmptySuccessNotTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Repositories!);
        Assert.False(result.IsTruncated);
    }

    // 2. Single-page success.
    [Fact]
    public async Task GetUserRepositoriesAsync_SinglePage_ReturnsAllRepositories()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                RepositoryArrayJson(MinimalRepositoryJson("mustafanazli/RepoPulse"), MinimalRepositoryJson("mustafanazli/Other")),
                Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Repositories!.Count);
        Assert.False(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    // 3. A full 100-record page (no next link) is still a single, non-truncated page.
    [Fact]
    public async Task GetUserRepositoriesAsync_FullPageWithoutNextLink_ReturnsAllAndIsNotTruncated()
    {
        var items = Enumerable.Range(1, 100).Select(i => MinimalRepositoryJson($"mustafanazli/Repo{i}")).ToArray();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(items), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Repositories!.Count);
        Assert.False(result.IsTruncated);
    }

    // 4. Multi-page success with order preserved across pages.
    [Fact]
    public async Task GetUserRepositoriesAsync_ThreePages_PreservesOrderAcrossPages()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return page switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A"), MinimalRepositoryJson("owner/B")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/C")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(3) } }
                },
                3 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/D")), Encoding.UTF8, "application/json")
                },
                _ => throw new InvalidOperationException("Unexpected page requested: " + page)
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsTruncated);
        Assert.Equal(["owner/A", "owner/B", "owner/C", "owner/D"], result.Repositories!.Select(r => r.FullName));
        Assert.Equal(3, handler.RequestCount);
    }

    // 5. No Link header at all — stop after the first page, not truncated.
    [Fact]
    public async Task GetUserRepositoriesAsync_NoLinkHeader_StopsAfterFirstPage()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    // 6. Case-insensitive duplicate repository across pages — first occurrence kept.
    [Fact]
    public async Task GetUserRepositoriesAsync_CaseInsensitiveDuplicateAcrossPages_Deduplicated()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return page switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("mustafanazli/RepoPulse")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        RepositoryArrayJson(MinimalRepositoryJson("MUSTAFANAZLI/REPOPULSE"), MinimalRepositoryJson("owner/Other")),
                        Encoding.UTF8, "application/json")
                },
                _ => throw new InvalidOperationException("Unexpected page requested: " + page)
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Repositories!.Count);
        Assert.Equal("mustafanazli/RepoPulse", result.Repositories![0].FullName);
        Assert.Equal("owner/Other", result.Repositories![1].FullName);
    }

    // 7. 10-page ceiling — an 11th page is never requested, and the result is truncated.
    [Fact]
    public async Task GetUserRepositoriesAsync_MoreThanTenPages_StopsAtTenAndIsTruncated()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson($"owner/Repo{page}")), Encoding.UTF8, "application/json"),
                Headers = { { "Link", NextLinkHeader(page + 1) } }
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(10, result.Repositories!.Count);
        Assert.Equal(10, handler.RequestCount);
    }

    // 8. A repeating next-link (loop) is detected and stopped, result truncated.
    [Fact]
    public async Task GetUserRepositoriesAsync_RepeatingNextLink_StopsAndIsTruncated()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return page switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    // Loops back to page 2 again instead of advancing.
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/B")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                _ => throw new InvalidOperationException("Unexpected page requested: " + page)
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(2, result.Repositories!.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    // 9. A malformed Link header (a rel="next" entry with an unparsable URL) is treated as untrusted.
    [Fact]
    public async Task GetUserRepositoriesAsync_MalformedLinkHeader_TreatedAsUntrustedAndTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", "<not-a-valid-uri>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Single(result.Repositories!);
        Assert.Equal(1, handler.RequestCount);
    }

    // 10. A next-link that fails scheme/host/path/port/userinfo/fragment/query checks is never followed.
    [Theory]
    [InlineData("<http://api.github.com/user/repos?page=2&per_page=100>; rel=\"next\"")] // not HTTPS
    [InlineData("<https://evil.example.com/user/repos?page=2&per_page=100>; rel=\"next\"")] // wrong host
    [InlineData("<https://api.github.com:8443/user/repos?page=2&per_page=100>; rel=\"next\"")] // explicit port
    [InlineData("<https://user:pass@api.github.com/user/repos?page=2&per_page=100>; rel=\"next\"")] // userinfo
    [InlineData("<https://api.github.com/user/repos?page=2&per_page=100#frag>; rel=\"next\"")] // fragment
    [InlineData("<https://api.github.com/other/path?page=2&per_page=100>; rel=\"next\"")] // wrong path
    [InlineData("<https://api.github.com/user/repos?page=2&per_page=100&evil=1>; rel=\"next\"")] // unexpected query param
    public async Task GetUserRepositoriesAsync_UntrustedNextLink_IsRejectedAndTruncated(string linkHeaderValue)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", linkHeaderValue } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Single(result.Repositories!);
        Assert.Equal(1, handler.RequestCount);
    }

    // 11. 401.
    [Fact]
    public async Task GetUserRepositoriesAsync_Unauthorized_ReturnsUnauthorizedFailureKind()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unauthorized, result.FailureKind);
    }

    // 12. 403 / 429 rate limit.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task GetUserRepositoriesAsync_ForbiddenOrTooManyRequests_ReturnsRateLimitedFailureKind(HttpStatusCode statusCode)
    {
        const string marker = "RATE-LIMIT-BODY-MARKER-list-9f3a";
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent($$"""{"message":"{{marker}}"}""", Encoding.UTF8, "application/json")
            };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.RateLimited, result.FailureKind);
        Assert.Null(result.Repositories);
    }

    // 13. Network failure / WebException on the first page.
    [Fact]
    public async Task GetUserRepositoriesAsync_FirstPageNetworkException_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NetworkError, result.FailureKind);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_FirstPageWebException_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new WebException("Socket closed"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NetworkError, result.FailureKind);
    }

    // 14. A failure on the second page is reported as a typed failure, never a silent partial success.
    [Fact]
    public async Task GetUserRepositoriesAsync_SecondPageHttpError_ReturnsTypedFailureNotPartialSuccess()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            var page = GetRequestedPage(request);
            if (page == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unauthorized, result.FailureKind);
        Assert.Null(result.Repositories);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_SecondPageNetworkException_ReturnsTypedFailureNotPartialSuccess()
    {
        var page1Served = false;
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (!page1Served)
            {
                page1Served = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                };
            }

            throw new HttpRequestException("connection reset");
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.NetworkError, result.FailureKind);
        Assert.Null(result.Repositories);
    }

    // 15. Malformed JSON.
    [Fact]
    public async Task GetUserRepositoriesAsync_MalformedJson_ReturnsUnexpectedFailureAndDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not valid", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unexpected, result.FailureKind);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NonArrayJson_ReturnsUnexpectedFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message":"not an array"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubRepositoryFailureKind.Unexpected, result.FailureKind);
    }

    // 16. Null description/language/date fields map safely to null.
    [Fact]
    public async Task GetUserRepositoriesAsync_NullDescriptionLanguageAndDates_MapToNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(RepositoryJsonWithExplicitNulls("owner/A")), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var repository = Assert.Single(result.Repositories!);
        Assert.Null(repository.Description);
        Assert.Null(repository.PrimaryLanguage);
        Assert.Null(repository.UpdatedAt);
        Assert.Null(repository.PushedAt);
    }

    // 17. A record missing a required field (full_name) is skipped, not fatal to the whole page —
    // but the result must never claim to be complete when a record was dropped.
    [Fact]
    public async Task GetUserRepositoriesAsync_RecordMissingRequiredField_IsSkippedAndResultIsTruncated()
    {
        const string missingFullName = """
            {
              "name": "Broken",
              "owner": { "login": "owner" },
              "html_url": "https://github.com/owner/Broken",
              "default_branch": "main"
            }
            """;
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                RepositoryArrayJson(MinimalRepositoryJson("owner/Good"), missingFullName),
                Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var repository = Assert.Single(result.Repositories!);
        Assert.Equal("owner/Good", repository.FullName);
        // The dropped record must be signalled — a silent shrink with
        // IsTruncated still false would let the caller believe the list is
        // complete when it is not.
        Assert.True(result.IsTruncated);
    }

    // 18. Cancellation propagates rather than being swallowed as a generic failure.
    [Fact]
    public async Task GetUserRepositoriesAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ThrowingHttpMessageHandler(new OperationCanceledException());
        var client = new GitHubApiClient(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetUserRepositoriesAsync("test-access-token", cts.Token));
    }

    // 19. The token never appears anywhere except the Authorization header.
    [Fact]
    public async Task GetUserRepositoriesAsync_TokenNeverAppearsOutsideAuthorizationHeader()
    {
        const string token = "SUPER-SECRET-TOKEN-list-abc123";
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return page switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/B")), Encoding.UTF8, "application/json")
                }
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync(token, CancellationToken.None);

        Assert.True(result.IsSuccess);
        foreach (var request in handler.Requests)
        {
            Assert.DoesNotContain(token, request.RequestUri!.ToString());
            Assert.Null(request.Content);
            Assert.Equal(token, request.Headers.Authorization!.Parameter);
        }
    }

    // 20. Every request (including subsequent pages) carries the required GitHub headers.
    [Fact]
    public async Task GetUserRepositoriesAsync_AllRequests_CarryRequiredGitHubHeaders()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return page switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/B")), Encoding.UTF8, "application/json")
                }
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        foreach (var request in handler.Requests)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.github.com", request.RequestUri.Host);
            Assert.Equal("/user/repos", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("RepoPulse", request.Headers.UserAgent.ToString());
            Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
        }
    }

    // First request must use the documented sort/direction/per_page contract.
    [Fact]
    public async Task GetUserRepositoriesAsync_FirstRequest_UsesExpectedQueryContract()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.Equal(
            "https://api.github.com/user/repos?sort=updated&direction=desc&per_page=100",
            handler.LastRequest!.RequestUri!.ToString());
    }

    // --- Pagination integrity hardening (targeted post-PR #11 audit) ---
    // A next-link that is present but does not pass every one of these
    // checks must never be followed, must never crash, and must always
    // leave the result marked IsTruncated=true — the caller must never be
    // told an incomplete list is complete.

    [Fact]
    public async Task GetUserRepositoriesAsync_NextLinkSkipsAPage_RejectedAndDoesNotFollow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            // Current page is 1 — a valid next link would say page=2, not page=3.
            Headers = { { "Link", NextLinkHeader(3) } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Single(result.Repositories!);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task GetUserRepositoriesAsync_NextLinkPageZeroOrNegative_RejectedAndTruncated(string pageValue)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", $"<https://api.github.com/user/repos?page={pageValue}&per_page=100>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextLinkDuplicatePageKey_RejectedAndTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", "<https://api.github.com/user/repos?page=2&page=2&per_page=100>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextLinkDuplicateNonPageKey_RejectedAndTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", "<https://api.github.com/user/repos?page=2&per_page=100&per_page=100>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextLinkUnexpectedPerPageValue_RejectedAndTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", "<https://api.github.com/user/repos?page=2&per_page=99>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextLinkUnexpectedSortValue_RejectedAndTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", "<https://api.github.com/user/repos?page=2&per_page=100&sort=name>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetUserRepositoriesAsync_NextLinkUnexpectedDirectionValue_RejectedAndTruncated()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", "<https://api.github.com/user/repos?page=2&per_page=100&direction=asc>; rel=\"next\"" } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("<https://api.github.com/user/repos?page=2&per_page=>; rel=\"next\"")] // empty value
    [InlineData("<https://api.github.com/user/repos?page=2&=100>; rel=\"next\"")] // empty key
    [InlineData("<https://api.github.com/user/repos?page=2&&per_page=100>; rel=\"next\"")] // stray "&&"
    public async Task GetUserRepositoriesAsync_NextLinkEmptyKeyOrValue_RejectedAndTruncated(string linkHeaderValue)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            Headers = { { "Link", linkHeaderValue } }
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsTruncated);
        Assert.Equal(1, handler.RequestCount);
    }

    // A clean, strictly-sequential two-page flow must still work after the
    // stricter next-link validation above.
    [Fact]
    public async Task GetUserRepositoriesAsync_NormalTwoPageFlow_StillWorksAfterValidationHardening()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var page = GetRequestedPage(request);
            return page switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
                    Headers = { { "Link", NextLinkHeader(2) } }
                },
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(MinimalRepositoryJson("owner/B")), Encoding.UTF8, "application/json")
                },
                _ => throw new InvalidOperationException("Unexpected page requested: " + page)
            };
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetUserRepositoriesAsync("test-access-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsTruncated);
        Assert.Equal(["owner/A", "owner/B"], result.Repositories!.Select(r => r.FullName));
        Assert.Equal(2, handler.RequestCount);
    }

    // --- GetLatestRepositoryCommitAsync (RP-013) ---
    // No test here ever contacts the real GitHub network — every call goes
    // through FakeHttpMessageHandler/ThrowingHttpMessageHandler, and no real
    // GitHub token is ever used, only obviously-fake fixtures.

    private static string SingleCommitJson(string? committerDate, string? authorDate, string sha = "abc1234567890def", string? message = "Fix parser bug\n\nLonger body here.")
    {
        var authorPart = authorDate is null ? "" : $"\"date\":\"{authorDate}\"";
        var committerPart = committerDate is null ? "" : $"\"date\":\"{committerDate}\"";
        var messagePart = message is null ? "" : $",\"message\":\"{JsonEscape(message)}\"";

        return $"[{{\"sha\":\"{sha}\",\"commit\":{{\"author\":{{{authorPart}}},\"committer\":{{{committerPart}}}{messagePart}}}}}]";
    }

    private static string JsonEscape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    // 1. Correct endpoint and per_page=1.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_SendsExpectedRequestWithPerPageOne()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("https://api.github.com/repos/mustafanazli/RepoPulse/commits?per_page=1", request.RequestUri!.ToString());
    }

    // 2. Owner/repository path encoding.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_EncodesOwnerAndRepositorySegments()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetLatestRepositoryCommitAsync("test-access-token", "owner name", "repo/name", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("https://api.github.com/repos/owner%20name/repo%2Fname/commits?per_page=1", request.RequestUri!.AbsoluteUri);
    }

    // 3. Authorization header present, token never in the query string.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_TokenOnlyInAuthorizationHeader_NeverInQuery()
    {
        const string token = "SUPER-SECRET-TOKEN-commit-abc123";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        await client.GetLatestRepositoryCommitAsync(token, "mustafanazli", "RepoPulse", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.DoesNotContain(token, request.RequestUri!.ToString());
        Assert.Null(request.Content);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(token, request.Headers.Authorization.Parameter);
        Assert.Equal("RepoPulse", request.Headers.UserAgent.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
    }

    // 4. 200 with one commit → committer date used.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_SingleCommit_UsesCommitterDate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", "2026-01-10T09:00:00Z"), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.HasCommits);
        Assert.Equal(DateTimeOffset.Parse("2026-01-15T10:30:00Z"), result.Commit!.CommittedAtUtc);
    }

    // 5. Committer date missing → falls back to author date.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_CommitterDateMissing_FallsBackToAuthorDate()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson(null, "2026-01-10T09:00:00Z"), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.HasCommits);
        Assert.Equal(DateTimeOffset.Parse("2026-01-10T09:00:00Z"), result.Commit!.CommittedAtUtc);
    }

    // 6. 200 with empty array → NoCommits, a success shape.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_EmptyArray_ReturnsNoCommitsSuccess()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "EmptyRepo", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.HasCommits);
        Assert.Null(result.Commit);
        Assert.Null(result.FailureKind);
    }

    // 7. GitHub's 409 "empty repository" → also NoCommits, not a failure.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_Conflict409_ReturnsNoCommitsSuccess()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)409)
        {
            Content = new StringContent("""{"message":"Git Repository is empty."}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "EmptyRepo", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.HasCommits);
        Assert.Null(result.Commit);
    }

    // 8. 401 → Unauthorized.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_Unauthorized_ReturnsUnauthorizedFailureKind()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.Unauthorized, result.FailureKind);
    }

    // 9 & 10. 403 and 429 → RateLimited.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task GetLatestRepositoryCommitAsync_ForbiddenOrTooManyRequests_ReturnsRateLimitedFailureKind(HttpStatusCode statusCode)
    {
        const string marker = "RATE-LIMIT-BODY-MARKER-commit-9f3a";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent($$"""{"message":"{{marker}}"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.RateLimited, result.FailureKind);
        Assert.Null(result.Commit);
    }

    // 11. 404 → NotFound.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_NotFound_ReturnsNotFoundFailureKind()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"message":"Not Found"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "does-not-exist", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.NotFound, result.FailureKind);
    }

    // 12. HttpRequestException → NetworkError.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_NetworkException_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.NetworkError, result.FailureKind);
    }

    // 13. WebException → NetworkError (Xamarin.Android raw-socket-failure shape).
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_WebException_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new WebException("Socket closed"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.NetworkError, result.FailureKind);
    }

    [Fact]
    public async Task GetLatestRepositoryCommitAsync_Timeout_ReturnsNetworkErrorFailureKindSafely()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("simulated timeout"));
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.NetworkError, result.FailureKind);
    }

    // 14. Malformed JSON → Unexpected.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_MalformedJson_ReturnsUnexpectedFailureAndDoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not valid", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.Unexpected, result.FailureKind);
    }

    // 15. Non-array JSON (e.g. a single object body) → Unexpected.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_NonArrayJson_ReturnsUnexpectedFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message":"not an array"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.Unexpected, result.FailureKind);
    }

    // 16. A record is present but neither committer.date nor author.date is
    // usable → Unexpected, never a fabricated date and never pushed_at/
    // updated_at as a silent substitute.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_RecordWithoutEitherDate_ReturnsUnexpectedFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson(null, null), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubLatestCommitFailureKind.Unexpected, result.FailureKind);
    }

    // 17. Raw error bodies / the token never leak into the typed result.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_ErrorResponse_DoesNotExposeRawBodyOrToken()
    {
        const string token = "SUPER-SECRET-TOKEN-commit-error-abc123";
        const string marker = "RAW-ERROR-BODY-MARKER-9f3a";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent($$"""{"message":"{{marker}}"}""", Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync(token, "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.False(result.IsSuccess);
        // The typed result carries no free-text field at all for failures —
        // only the enum — so there is structurally nowhere for the raw body
        // or the token to leak into.
        Assert.Null(result.Commit);
        var request = handler.LastRequest!;
        Assert.DoesNotContain(token, request.RequestUri!.ToString());
    }

    // 18. Cancellation propagates rather than being swallowed as a generic failure.
    [Fact]
    public async Task GetLatestRepositoryCommitAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ThrowingHttpMessageHandler(new OperationCanceledException());
        var client = new GitHubApiClient(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", cts.Token));
    }

    // --- Data minimization (RP-013 audit) ---
    //
    // GitHubLatestCommit carries ONLY CommittedAtUtc — RepositoryDetailPage
    // never shows a SHA or commit message, so the parser must never extract,
    // retain, or leak either, no matter what shape GitHub's own "sha"/
    // "message" fields take in the raw response.

    [Fact]
    public void GitHubLatestCommit_HasExactlyOneProperty_CommittedAtUtc()
    {
        var properties = typeof(GitHubLatestCommit).GetProperties();

        var property = Assert.Single(properties);
        Assert.Equal(nameof(GitHubLatestCommit.CommittedAtUtc), property.Name);
    }

    [Fact]
    public async Task GetLatestRepositoryCommitAsync_VeryLongCommitMessage_IsIgnoredAndNeverSurfaced()
    {
        var longMessage = new string('A', 50_000);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null, message: longMessage), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-01-15T10:30:00Z"), result.Commit!.CommittedAtUtc);
        Assert.DoesNotContain(longMessage, result.Commit.ToString());
    }

    [Fact]
    public async Task GetLatestRepositoryCommitAsync_MessageWithControlCharacters_IsIgnoredAndNeverSurfaced()
    {
        const string controlCharacterMessage = "line one\r\nline two\tembedded-tabbell";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null, message: controlCharacterMessage), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-01-15T10:30:00Z"), result.Commit!.CommittedAtUtc);
        Assert.DoesNotContain("embedded-tab", result.Commit.ToString());
    }

    [Fact]
    public async Task GetLatestRepositoryCommitAsync_InvalidShaShape_IsIgnoredAndDoesNotFail()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null, sha: "not-a-valid-hex-sha!!"), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        // An invalid/unexpected "sha" shape has no bearing on the outcome —
        // it is simply never read.
        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-01-15T10:30:00Z"), result.Commit!.CommittedAtUtc);
    }

    [Fact]
    public async Task GetLatestRepositoryCommitAsync_RawShaAndMessage_NeverAppearInResultToString()
    {
        const string marker = "MARKER-COMMIT-MESSAGE-CONTENT-9f3a";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SingleCommitJson("2026-01-15T10:30:00Z", null, sha: "0123456789abcdef0123456789abcdef01234567", message: marker), Encoding.UTF8, "application/json")
        });
        var client = new GitHubApiClient(new HttpClient(handler));

        var result = await client.GetLatestRepositoryCommitAsync("test-access-token", "mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(result.IsSuccess);
        // GitHubLatestCommit is a record with a single DateTimeOffset
        // property — ToString() structurally cannot contain the raw sha or
        // message, since neither is a property on the type at all.
        Assert.DoesNotContain(marker, result.Commit!.ToString());
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef01234567", result.Commit.ToString());
    }
}
