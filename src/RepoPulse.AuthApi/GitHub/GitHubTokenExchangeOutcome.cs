using RepoPulse.AuthApi.Contracts;

namespace RepoPulse.AuthApi.GitHub;

public enum GitHubTokenExchangeFailureKind
{
    OAuthRejected,
    UpstreamError,
    UpstreamTimeout
}

public sealed class GitHubTokenExchangeOutcome
{
    public bool IsSuccess { get; }
    public GitHubTokenExchangeResponse? Success { get; }
    public GitHubTokenExchangeFailureKind FailureKind { get; }

    private GitHubTokenExchangeOutcome(bool isSuccess, GitHubTokenExchangeResponse? success, GitHubTokenExchangeFailureKind failureKind)
    {
        IsSuccess = isSuccess;
        Success = success;
        FailureKind = failureKind;
    }

    public static GitHubTokenExchangeOutcome Ok(GitHubTokenExchangeResponse response) =>
        new(true, response, default);

    public static GitHubTokenExchangeOutcome Failure(GitHubTokenExchangeFailureKind kind) =>
        new(false, null, kind);
}
