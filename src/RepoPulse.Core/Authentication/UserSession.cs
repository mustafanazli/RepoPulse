namespace RepoPulse.Core.Authentication;

// The signed-in user's identity + tokens, held only by UserSessionStore.
// AccessToken/RefreshToken are exactly the values RepoPulseAuthApiClient
// returned at exchange time — never re-derived. AccessTokenExpiresAtUtc is
// null when GitHub didn't report an expires_in for this token (RP-008);
// SessionPersistenceStore is the only thing that writes this record to
// durable storage (SecureStorage) — see PersistedSessionPayload.
public sealed record UserSession(
    string AccessToken,
    string? RefreshToken,
    string Login,
    string? AvatarUrl,
    DateTimeOffset? AccessTokenExpiresAtUtc = null);
