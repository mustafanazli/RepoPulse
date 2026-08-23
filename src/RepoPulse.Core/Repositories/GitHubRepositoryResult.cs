namespace RepoPulse.Core.Repositories;

// Mirrors AuthApiExchangeResult's typed-failure-kind pattern: the UI layer
// maps each kind to a short, safe, user-facing message — never a raw
// GitHub response body or exception message.
public enum GitHubRepositoryFailureKind
{
    NotFound,
    Unauthorized,
    RateLimited,
    NetworkError,
    Unexpected
}

public sealed class GitHubRepositoryResult
{
    public bool IsSuccess { get; }
    public GitHubRepository? Repository { get; }
    public GitHubRepositoryFailureKind? FailureKind { get; }

    private GitHubRepositoryResult(bool isSuccess, GitHubRepository? repository, GitHubRepositoryFailureKind? failureKind)
    {
        IsSuccess = isSuccess;
        Repository = repository;
        FailureKind = failureKind;
    }

    public static GitHubRepositoryResult Success(GitHubRepository repository) => new(true, repository, null);

    public static GitHubRepositoryResult Failure(GitHubRepositoryFailureKind failureKind) => new(false, null, failureKind);
}
