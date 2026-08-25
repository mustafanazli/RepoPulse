namespace RepoPulse.Core.Authentication;

// Non-sensitive, boolean, app-specific fallback signal used ONLY when
// SessionPersistenceStore cannot confirm the persisted session key was
// actually removed (SecureStorage's Remove threw). Never holds a token, a
// token hash, or any fragment of one — just "the last known session must
// not be trusted again, even if its bytes are still on disk," until a new
// sign-in is successfully persisted. Deliberately a separate, much simpler
// storage mechanism than SecureStorage (e.g. MAUI Preferences) so it isn't
// exposed to the same failure mode (Android Keystore/backup corruption)
// that this marker exists to cover for.
public interface ISessionInvalidationMarker
{
    Task<bool> IsSetAsync();

    Task SetAsync();

    Task ClearAsync();
}
