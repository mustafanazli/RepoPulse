namespace RepoPulse.Core.Authentication;

public sealed record GitHubUser(string Login, string? AvatarUrl);

public sealed class GitHubUserResult
{
    public bool IsSuccess { get; }
    public GitHubUser? User { get; }
    public string? SafeErrorMessage { get; }

    private GitHubUserResult(bool isSuccess, GitHubUser? user, string? safeErrorMessage)
    {
        IsSuccess = isSuccess;
        User = user;
        SafeErrorMessage = safeErrorMessage;
    }

    public static GitHubUserResult Success(GitHubUser user) => new(true, user, null);

    public static GitHubUserResult Failure(string safeErrorMessage) => new(false, null, safeErrorMessage);
}
