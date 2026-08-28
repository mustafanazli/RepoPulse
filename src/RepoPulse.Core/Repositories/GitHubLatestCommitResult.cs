namespace RepoPulse.Core.Repositories;

// RP-013: typed failures for GET /repos/{owner}/{repo}/commits?per_page=1 —
// deliberately its own enum rather than reusing GitHubRepositoryFailureKind,
// since "the repository exists but has zero commits" is a SUCCESS here (see
// GitHubLatestCommitResult.NoCommits), not a failure shape shared with the
// single-repository lookup.
public enum GitHubLatestCommitFailureKind
{
    Unauthorized,
    RateLimited,
    NotFound,
    NetworkError,
    Unexpected
}

// Owner/name/repository identity is deliberately NOT carried here — a caller
// already has that from the GitHubRepository it navigated with. Carries
// ONLY CommittedAtUtc — RepositoryDetailPage's "Son commit" label shows
// nothing else, so an earlier version's ShortSha/MessageSummary fields were
// dead data: GitHub's commit SHA and message are never retained, logged, or
// displayed anywhere in this app. Re-add a field here only when a real UI
// use appears, with its own validation/truncation at parse time.
public sealed record GitHubLatestCommit(DateTimeOffset CommittedAtUtc);

// Mirrors GitHubRepositoryResult's typed-failure pattern, but with a third,
// explicit success shape: a repository that exists and returned 200 with an
// empty commit array (or GitHub's 409 "empty repository" response) is
// IsSuccess with HasCommits=false — never conflated with an actual failure,
// and never with "loading"/"unknown".
public sealed class GitHubLatestCommitResult
{
    public bool IsSuccess { get; }
    public bool HasCommits { get; }
    public GitHubLatestCommit? Commit { get; }
    public GitHubLatestCommitFailureKind? FailureKind { get; }

    private GitHubLatestCommitResult(bool isSuccess, bool hasCommits, GitHubLatestCommit? commit, GitHubLatestCommitFailureKind? failureKind)
    {
        IsSuccess = isSuccess;
        HasCommits = hasCommits;
        Commit = commit;
        FailureKind = failureKind;
    }

    public static GitHubLatestCommitResult Success(GitHubLatestCommit commit) => new(true, true, commit, null);

    public static GitHubLatestCommitResult NoCommits() => new(true, false, null, null);

    public static GitHubLatestCommitResult Failure(GitHubLatestCommitFailureKind failureKind) => new(false, false, null, failureKind);
}
