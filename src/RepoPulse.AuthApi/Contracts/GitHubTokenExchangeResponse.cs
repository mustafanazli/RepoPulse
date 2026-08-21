namespace RepoPulse.AuthApi.Contracts;

// GitHub does not always return expires_in/refresh_token/refresh_token_expires_in
// (depends on the OAuth App's token expiration setting), so those are
// nullable here rather than required.
public sealed class GitHubTokenExchangeResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public string? Scope { get; init; }
    public int? ExpiresIn { get; init; }
    public string? RefreshToken { get; init; }
    public long? RefreshTokenExpiresIn { get; init; }
}
