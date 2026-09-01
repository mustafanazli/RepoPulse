namespace RepoPulse.Core.Repositories;

// RP-020: typed outcomes for GitHubApiClient.GetOldestOpenIssueAsync's GraphQL
// repository.issues(states: OPEN, first: 1, orderBy: CREATED_AT ASC) query.
// RepositoryUnavailable is deliberately its own value, never named
// "NotFound" — GraphQL's `data.repository == null` shape can mean the
// repository genuinely does not exist OR that the token simply lacks access
// to it; this type makes no claim about which. There is no NotFound value at
// all here (unlike GitHubRepositoryFailureKind/GitHubLatestCommitFailureKind)
// because the GraphQL endpoint does not surface "not found" as an HTTP 404 —
// it comes back as this null-repository shape instead.
public enum GitHubOldestOpenIssueFailureKind
{
    RepositoryUnavailable,
    Unauthorized,
    RateLimited,
    NetworkError,
    Unexpected
}

// Deliberately carries nothing beyond CreatedAtUtc: no owner/repository
// identity (the caller already has it), no token, no raw GraphQL response
// body, no query text, no HTTP headers, no GraphQL error message — a caller
// holding this result cannot leak anything beyond a single timestamp.
// Mirrors GitHubLatestCommitResult's (RP-013) IsSuccess/Has*/FailureKind
// shape: Success = a real open issue was found, NoOpenIssues = GitHub
// POSITIVELY CONFIRMED zero open issues (a real, successful, data-verified
// result — totalCount==0 with an empty nodes array — never conflated with a
// failure), Failure = the query could not be answered at all.
//
// Construction is intentionally not public-positional — only
// GitHubApiClient's own GraphQL response parsing should be able to produce a
// value here. Private constructor + three narrow factories (never a generic
// Create) means an external caller can never construct e.g. Success with a
// null CreatedAtUtc, or a Failure with HasOpenIssues true, or any other
// invalid combination.
public sealed class GitHubOldestOpenIssueResult
{
    public bool IsSuccess { get; }
    public bool HasOpenIssues { get; }
    public DateTimeOffset? CreatedAtUtc { get; }
    public GitHubOldestOpenIssueFailureKind? FailureKind { get; }

    private GitHubOldestOpenIssueResult(bool isSuccess, bool hasOpenIssues, DateTimeOffset? createdAtUtc, GitHubOldestOpenIssueFailureKind? failureKind)
    {
        IsSuccess = isSuccess;
        HasOpenIssues = hasOpenIssues;
        CreatedAtUtc = createdAtUtc;
        FailureKind = failureKind;
    }

    // Normalizes to UTC here — the one place this invariant is enforced —
    // so no caller, in this assembly or any other, can ever construct a
    // "successful" result whose CreatedAtUtc carries a non-zero offset. The
    // caller-supplied value is NOT assumed to already be UTC (a parsed
    // GraphQL timestamp may carry any offset); the instant is preserved
    // exactly, only the offset representation changes (e.g.
    // 2026-08-30T15:00:00+03:00 becomes 2026-08-30T12:00:00+00:00).
    public static GitHubOldestOpenIssueResult Success(DateTimeOffset createdAtUtc) => new(true, true, createdAtUtc.ToUniversalTime(), null);

    public static GitHubOldestOpenIssueResult NoOpenIssues() => new(true, false, null, null);

    public static GitHubOldestOpenIssueResult Failure(GitHubOldestOpenIssueFailureKind failureKind) => new(false, false, null, failureKind);
}
