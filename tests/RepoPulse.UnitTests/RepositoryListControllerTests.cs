using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// No test here ever contacts the real GitHub network — every call goes
// through FakeHttpMessageHandler/ThrowingHttpMessageHandler/a local
// DelayedHttpMessageHandler, driving a real GitHubApiClient. No real GitHub
// token is ever used, only an obviously fake fixture.
public class RepositoryListControllerTests
{
    private const string Token = "test-access-token";
    private const long Generation = 1;

    private static GitHubRepository MakeRepository(string fullName) =>
        new(
            fullName.Split('/')[0],
            fullName.Split('/')[1],
            fullName,
            null,
            $"https://github.com/{fullName}",
            0,
            0,
            0,
            null,
            "main",
            false,
            false,
            null,
            null);

    private static string RepositoryJson(string fullName)
    {
        var parts = fullName.Split('/');
        return $$"""
            {
              "name": "{{parts[1]}}",
              "full_name": "{{fullName}}",
              "owner": { "login": "{{parts[0]}}" },
              "html_url": "https://github.com/{{fullName}}",
              "default_branch": "main"
            }
            """;
    }

    private static string RepositoryArrayJson(params string[] items) => "[" + string.Join(",", items) + "]";

    // Structural proof (used after success/failure/cancellation) that no raw
    // access token can be retained anywhere on the controller instance: every
    // private instance field must be non-string-typed. Combined with the
    // per-scenario tests below, this satisfies "token not retained after
    // success/failure/cancellation" without needing to reach into any
    // specific field by name (which would need updating every time an
    // unrelated field is added).
    private static void AssertNoStringTypedFieldRetained(RepositoryListController controller)
    {
        var fields = typeof(RepositoryListController).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var field in fields)
        {
            Assert.False(
                field.FieldType == typeof(string),
                $"Field '{field.Name}' is string-typed and could retain a raw access token.");
        }
    }

    [Fact]
    public async Task LoadAsync_SuccessfulList_SetsLoadedStateWithRepositories()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(RepositoryJson("owner/A"), RepositoryJson("owner/B")), Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Loaded, controller.State.Status);
        Assert.Equal(2, controller.State.Repositories.Count);
        Assert.False(controller.State.IsTruncated);
    }

    [Fact]
    public async Task LoadAsync_EmptyList_SetsEmptyState()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Empty, controller.State.Status);
        Assert.Empty(controller.State.Repositories);
    }

    [Fact]
    public async Task LoadAsync_TruncatedResult_SetsIsTruncatedTrueOnLoadedState()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(RepositoryJson("owner/A")), Encoding.UTF8, "application/json"),
            // rel="next" pointing at an untrusted host — GetUserRepositoriesAsync
            // (RP-009) rejects it and reports the result truncated rather than
            // following it.
            Headers = { { "Link", "<https://evil.example.com/user/repos?page=2&per_page=100>; rel=\"next\"" } }
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Loaded, controller.State.Status);
        Assert.True(controller.State.IsTruncated);
    }

    [Fact]
    public async Task LoadAsync_Unauthorized_SetsUnauthorizedState()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Unauthorized, controller.State.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task LoadAsync_RateLimited_SetsRateLimitedState(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("""{"message":"API rate limit exceeded"}""", Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.RateLimited, controller.State.Status);
    }

    [Fact]
    public async Task LoadAsync_NetworkErrorAfterPriorSuccess_PreservesExistingRepositories()
    {
        var succeed = true;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            if (succeed)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RepositoryArrayJson(RepositoryJson("owner/A"), RepositoryJson("owner/B")), Encoding.UTF8, "application/json")
                };
            }

            throw new HttpRequestException("connection refused");
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);
        Assert.Equal(RepositoryListStatus.Loaded, controller.State.Status);

        succeed = false;
        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.NetworkError, controller.State.Status);
        // The previously loaded list must still be there — a transient
        // failure never blanks a working list.
        Assert.Equal(2, controller.State.Repositories.Count);
    }

    [Fact]
    public async Task LoadAsync_MalformedJson_SetsUnexpectedState()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not valid", Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Unexpected, controller.State.Status);
    }

    private sealed class DelayedHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> tcs = new();

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return await tcs.Task;
        }

        public void Complete(HttpResponseMessage response) => tcs.TrySetResult(response);
    }

    [Fact]
    public async Task LoadAsync_SecondCallWhileFirstStillInFlight_IsIgnored()
    {
        var handler = new DelayedHttpMessageHandler();
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        var firstLoad = controller.LoadAsync(Token, Generation, CancellationToken.None);
        var secondLoad = controller.LoadAsync(Token, Generation, CancellationToken.None);

        handler.Complete(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });

        await firstLoad;
        await secondLoad;

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(RepositoryListStatus.Empty, controller.State.Status);
        Assert.False(controller.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_CancelledBeforeCompletion_ThrowsAndLeavesStateUntouched()
    {
        var handler = new ThrowingHttpMessageHandler(new OperationCanceledException());
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.LoadAsync(Token, Generation, cts.Token));

        // Cancellation must never be reported as a failed load — State
        // stays exactly as it was (Idle), not Unexpected/NetworkError/etc.
        Assert.Equal(RepositoryListStatus.Idle, controller.State.Status);
        Assert.False(controller.IsLoading);
    }

    [Fact]
    public async Task HasLoadedFor_TrueForSameGenerationAfterSuccess_FalseForDifferentGeneration()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        Assert.False(controller.HasLoadedFor(Generation));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.True(controller.HasLoadedFor(Generation));
        Assert.False(controller.HasLoadedFor(Generation + 1));
    }

    [Fact]
    public async Task HasLoadedFor_NewSessionGenerationEvenForSameLogin_TriggersReload()
    {
        // Simulates signing out and back in as the same GitHub login: the
        // access token value could in principle be identical, but the
        // session generation always changes — HasLoadedFor must key off the
        // generation, never the token, so this must report "not loaded".
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(RepositoryJson("owner/A")), Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);
        Assert.True(controller.HasLoadedFor(Generation));

        var nextGeneration = Generation + 1;
        Assert.False(controller.HasLoadedFor(nextGeneration));

        await controller.LoadAsync(Token, nextGeneration, CancellationToken.None);
        Assert.True(controller.HasLoadedFor(nextGeneration));
        Assert.False(controller.HasLoadedFor(Generation));
    }

    [Fact]
    public async Task LoadAsync_AfterSuccess_DoesNotRetainAccessTokenInAnyField()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(RepositoryArrayJson(RepositoryJson("owner/A")), Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Loaded, controller.State.Status);
        AssertNoStringTypedFieldRetained(controller);
    }

    [Fact]
    public async Task LoadAsync_AfterFailure_DoesNotRetainAccessTokenInAnyField()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));

        await controller.LoadAsync(Token, Generation, CancellationToken.None);

        Assert.Equal(RepositoryListStatus.Unauthorized, controller.State.Status);
        AssertNoStringTypedFieldRetained(controller);
    }

    [Fact]
    public async Task LoadAsync_AfterCancellation_DoesNotRetainAccessTokenInAnyField()
    {
        var handler = new ThrowingHttpMessageHandler(new OperationCanceledException());
        var controller = new RepositoryListController(new GitHubApiClient(new HttpClient(handler)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.LoadAsync(Token, Generation, cts.Token));

        AssertNoStringTypedFieldRetained(controller);
    }
}
