namespace RepoPulse.Core.Authentication;

public sealed record TokenExchangeSuccess(string AccessToken, string TokenType, string? Scope);

public enum TokenExchangeFailureReason
{
    NetworkError,
    Timeout,
    NonSuccessStatusCode,
    MalformedResponse,
    OAuthError
}

public sealed class TokenExchangeResult
{
    public bool IsSuccess { get; }
    public TokenExchangeSuccess? Success { get; }
    public TokenExchangeFailureReason FailureReason { get; }

    // Deliberately generic — never built from raw response bodies, so it can
    // never carry a token, code, verifier, or state value.
    public string? SafeErrorMessage { get; }

    private TokenExchangeResult(bool isSuccess, TokenExchangeSuccess? success, TokenExchangeFailureReason failureReason, string? safeErrorMessage)
    {
        IsSuccess = isSuccess;
        Success = success;
        FailureReason = failureReason;
        SafeErrorMessage = safeErrorMessage;
    }

    public static TokenExchangeResult Ok(TokenExchangeSuccess success) =>
        new(true, success, default, null);

    public static TokenExchangeResult Failure(TokenExchangeFailureReason reason, string safeErrorMessage) =>
        new(false, null, reason, safeErrorMessage);
}
