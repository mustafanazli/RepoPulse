namespace RepoPulse.Core.Authentication;

// In-memory only — never persisted (see RP-003 report: SecureStorage is out of
// scope until RP-004, and a session must not outlive the app process anyway).
public sealed class AuthorizationSession
{
    public required string State { get; init; }
    public required string CodeVerifier { get; init; }
    public required string CodeChallenge { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
