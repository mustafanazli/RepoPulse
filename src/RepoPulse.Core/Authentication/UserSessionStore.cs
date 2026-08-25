namespace RepoPulse.Core.Authentication;

// App-level authenticated session — DISTINCT from AuthorizationSessionStore
// (which tracks a single in-flight OAuth PKCE attempt, not the signed-in
// user). Holds the access/refresh token + login/avatar of the currently
// signed-in user, in memory ONLY (RP-007) — never SecureStorage, SQLite, or
// Preferences. Cleared on sign-out and lost on app restart; that is a
// deliberate, documented RP-007 scope decision, not an oversight — see
// docs and RepoPulse-Project-Plan.md's RP-007 entry.
//
// Registered as a DI singleton (see MauiProgram.cs) so every page shares
// exactly one instance; thread-safe via a simple lock since navigation
// callbacks and Shell page construction can occur off the calling
// synchronization context.
public sealed class UserSessionStore
{
    private readonly object gate = new();
    private UserSession? current;
    private long sessionGeneration;

    public bool IsSignedIn
    {
        get { lock (gate) { return current is not null; } }
    }

    public UserSession? Current
    {
        get { lock (gate) { return current; } }
    }

    // Non-sensitive, monotonically increasing counter — bumped on every
    // SignIn (a fresh sign-in AND a cold-start SessionPersistenceStore
    // restore both call SignIn) and every SignOut, so any two distinct
    // sessions compare unequal, including signing out and back in as the
    // very same GitHub login. Never derived from the token, never a hash of
    // it — safe to hold, compare, or log freely. Callers (e.g.
    // RepositoryListController, RP-010) use this instead of the raw access
    // token as a cache/session identity, so a token is never retained
    // outside UserSessionStore itself.
    public long SessionGeneration
    {
        get { lock (gate) { return sessionGeneration; } }
    }

    public void SignIn(UserSession session)
    {
        lock (gate)
        {
            current = session;
            sessionGeneration++;
        }
    }

    // Clears every in-memory field of the session (access token, refresh
    // token, login, avatar) — nothing is left behind for a later page to
    // accidentally read after sign-out. Also invalidates SessionGeneration,
    // so anything cached "for" the just-cleared session is recognized as
    // stale even before a new sign-in happens.
    public void SignOut()
    {
        lock (gate)
        {
            current = null;
            sessionGeneration++;
        }
    }
}
