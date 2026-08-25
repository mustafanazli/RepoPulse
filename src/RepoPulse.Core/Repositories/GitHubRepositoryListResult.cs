namespace RepoPulse.Core.Repositories;

// Reuses GitHubRepositoryFailureKind (RP-006) rather than a parallel enum —
// NotFound simply never occurs for this call. IsTruncated is a distinct,
// explicit signal from IsSuccess: a truncated result is still success (the
// caller got real data), just capped by GetUserRepositoriesAsync's own
// page/repository ceiling rather than by GitHub actually running out of
// pages.
public sealed class GitHubRepositoryListResult
{
    public bool IsSuccess { get; }
    public IReadOnlyList<GitHubRepository>? Repositories { get; }
    public bool IsTruncated { get; }
    public GitHubRepositoryFailureKind? FailureKind { get; }

    private GitHubRepositoryListResult(
        bool isSuccess,
        IReadOnlyList<GitHubRepository>? repositories,
        bool isTruncated,
        GitHubRepositoryFailureKind? failureKind)
    {
        IsSuccess = isSuccess;
        Repositories = repositories;
        IsTruncated = isTruncated;
        FailureKind = failureKind;
    }

    public static GitHubRepositoryListResult Success(IReadOnlyList<GitHubRepository> repositories, bool isTruncated) =>
        new(true, repositories, isTruncated, null);

    public static GitHubRepositoryListResult Failure(GitHubRepositoryFailureKind failureKind) =>
        new(false, null, false, failureKind);
}
