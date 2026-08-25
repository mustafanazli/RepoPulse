namespace RepoPulse.Core.Repositories;

// Every outcome RepositoryListPage (RP-010) needs to render. Deliberately
// separate from GitHubRepositoryListResult/GitHubRepositoryFailureKind —
// this is UI-facing display state (adds Loading/Empty), not the API
// contract itself.
public enum RepositoryListStatus
{
    Idle,
    Loading,
    Loaded,
    Empty,
    Unauthorized,
    RateLimited,
    NetworkError,
    Unexpected
}

public sealed class RepositoryListState
{
    public static readonly RepositoryListState Idle = new(RepositoryListStatus.Idle, Array.Empty<GitHubRepository>(), false);

    public RepositoryListStatus Status { get; }
    public IReadOnlyList<GitHubRepository> Repositories { get; }
    public bool IsTruncated { get; }

    public RepositoryListState(RepositoryListStatus status, IReadOnlyList<GitHubRepository> repositories, bool isTruncated)
    {
        Status = status;
        Repositories = repositories;
        IsTruncated = isTruncated;
    }
}
