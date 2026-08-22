namespace RepoPulse.Core.Authentication;

public sealed record AuthApiTokenResponse(
    string AccessToken,
    string TokenType,
    string? Scope,
    int? ExpiresIn,
    string? RefreshToken,
    long? RefreshTokenExpiresIn);

// Mirrors RepoPulse.AuthApi's error contract (docs/backend-auth.md) — the
// client classifies by the backend's "title" field (or HTTP status as a
// fallback) rather than ever surfacing the raw response body.
public enum AuthApiExchangeFailureKind
{
    InvalidRequest,
    OAuthExchangeFailed,
    UpstreamError,
    UpstreamTimeout,
    RateLimited,
    InternalError,

    // Client-side classifications: the backend was never reached, or its
    // response could not be understood at all.
    NetworkError,
    Timeout,
    MalformedResponse
}

public sealed class AuthApiExchangeResult
{
    public bool IsSuccess { get; }
    public AuthApiTokenResponse? Success { get; }
    public AuthApiExchangeFailureKind FailureKind { get; }

    private AuthApiExchangeResult(bool isSuccess, AuthApiTokenResponse? success, AuthApiExchangeFailureKind failureKind)
    {
        IsSuccess = isSuccess;
        Success = success;
        FailureKind = failureKind;
    }

    public static AuthApiExchangeResult Ok(AuthApiTokenResponse response) =>
        new(true, response, default);

    public static AuthApiExchangeResult Failure(AuthApiExchangeFailureKind kind) =>
        new(false, null, kind);
}
