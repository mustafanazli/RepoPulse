namespace RepoPulse.Core.Repositories;

// RP-016: typed failures for GET /repos/{owner}/{repository}/commits?since=..&until=..&per_page=1
// — deliberately its own enum rather than reusing GitHubLatestCommitFailureKind
// (RP-013), which has no shape for "the caller asked for an invalid time
// range" (InvalidRange is a caller-input problem resolved before any network
// call, not a GitHub response outcome).
public enum GitHubCommitCountFailureKind
{
    InvalidRange,
    NotFound,
    Unauthorized,
    RateLimited,
    NetworkError,
    Unexpected
}

// Count of commits reachable from a repository's default branch within a
// caller-supplied [sinceUtc, untilUtc) window. Deliberately carries nothing
// else: no owner/repository identity (the caller already has that), no
// token, no raw response body, no URL, no Link header value — a caller
// holding this result cannot leak anything beyond a single non-negative
// integer.
//
// Construction is intentionally not public-positional: Count is only
// meaningful when IsSuccess is true, and only GitHubApiClient's parsing
// logic should be able to produce a value here (never token/response
// content). Private constructor + factory mirrors GitHubLatestCommitResult
// (RP-013) and GitHubRepositoryResult (RP-006).
public sealed class GitHubCommitCountResult
{
    public bool IsSuccess { get; }
    public int? Count { get; }
    public GitHubCommitCountFailureKind? FailureKind { get; }

    private GitHubCommitCountResult(bool isSuccess, int? count, GitHubCommitCountFailureKind? failureKind)
    {
        IsSuccess = isSuccess;
        Count = count;
        FailureKind = failureKind;
    }

    public static GitHubCommitCountResult Success(int count)
    {
        if (count < 0)
        {
            // A negative count can only come from a parsing bug in the
            // caller (GitHubApiClient) — never from GitHub's response shape
            // itself, since every path that produces a value here already
            // validates it is a positive page number or a literal 0/1.
            throw new ArgumentOutOfRangeException(nameof(count), count, "Commit count cannot be negative.");
        }

        return new GitHubCommitCountResult(true, count, null);
    }

    public static GitHubCommitCountResult Failure(GitHubCommitCountFailureKind failureKind) =>
        new(false, null, failureKind);
}
