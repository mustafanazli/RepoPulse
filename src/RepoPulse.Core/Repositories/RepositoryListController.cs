using RepoPulse.Core.Authentication;

namespace RepoPulse.Core.Repositories;

// MAUI-independent presentation-state controller for RepositoryListPage
// (RP-010) — keeps every non-visual decision (concurrency guard, whether a
// reload is needed, failed-load-must-not-discard-existing-data, cancellation
// vs. failure) testable without a running MAUI host, exactly like
// SessionPersistenceStore (RP-008) does for session persistence. The page
// itself only ever reads State after awaiting LoadAsync and renders it.
public sealed class RepositoryListController
{
    private readonly IGitHubApiClient gitHubApiClient;
    private string? loadedForAccessToken;

    public RepositoryListController(IGitHubApiClient gitHubApiClient)
    {
        this.gitHubApiClient = gitHubApiClient;
    }

    public RepositoryListState State { get; private set; } = RepositoryListState.Idle;

    public bool IsLoading { get; private set; }

    // True once a load has completed successfully (even to an empty list)
    // for exactly this access token — RepositoryListPage's OnAppearing uses
    // this to decide whether a reload is needed at all, so returning from
    // RepositoryDetailPage (same token) never re-fetches, while a fresh
    // sign-in (a new token) always does.
    public bool HasLoadedFor(string accessToken) =>
        State.Status is RepositoryListStatus.Loaded or RepositoryListStatus.Empty && loadedForAccessToken == accessToken;

    // A second overlapping call while one is already in flight is a no-op —
    // the caller is expected to simply await the same in-flight result via
    // its own guard, but even if it doesn't, this never issues a second HTTP
    // request. A cancellation (the caller's token, e.g. the page navigating
    // away) propagates to the caller as-is and never touches State — only a
    // resolved GitHubRepositoryListResult ever changes it, so cancellation
    // can never look like a failed load.
    public async Task LoadAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var result = await gitHubApiClient.GetUserRepositoriesAsync(accessToken, cancellationToken);
            State = MapResult(result, accessToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private RepositoryListState MapResult(GitHubRepositoryListResult result, string accessToken)
    {
        if (result.IsSuccess)
        {
            var repositories = result.Repositories ?? Array.Empty<GitHubRepository>();
            loadedForAccessToken = accessToken;
            var status = repositories.Count == 0 ? RepositoryListStatus.Empty : RepositoryListStatus.Loaded;
            return new RepositoryListState(status, repositories, result.IsTruncated);
        }

        var failureStatus = result.FailureKind switch
        {
            GitHubRepositoryFailureKind.Unauthorized => RepositoryListStatus.Unauthorized,
            GitHubRepositoryFailureKind.RateLimited => RepositoryListStatus.RateLimited,
            GitHubRepositoryFailureKind.NetworkError => RepositoryListStatus.NetworkError,
            _ => RepositoryListStatus.Unexpected
        };

        // A failed reload never discards a previously loaded list — whatever
        // State.Repositories already held stays visible under the new
        // failure status, so a transient error never blanks a working list.
        return new RepositoryListState(failureStatus, State.Repositories, false);
    }
}
