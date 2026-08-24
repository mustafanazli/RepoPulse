namespace RepoPulse.Core.Authentication;

// The signed-in user's identity + tokens, held only by UserSessionStore.
// AccessToken/RefreshToken are exactly the values RepoPulseAuthApiClient
// returned at exchange time — never re-derived, never persisted anywhere
// beyond this in-memory record (RP-007: no SecureStorage/SQLite yet).
public sealed record UserSession(string AccessToken, string? RefreshToken, string Login, string? AvatarUrl);
