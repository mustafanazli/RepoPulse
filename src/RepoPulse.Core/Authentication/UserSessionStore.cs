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

    // Reading SessionGeneration and Current as two separate lock acquisitions
    // (as callers previously did) cannot guarantee they describe the same
    // sign-in — a SignOut/SignIn can land between the two reads. Callers that
    // must later verify, after an await, whether "their" session is still the
    // active one (FavoriteToggleController's cross-session race fix) need
    // both values captured together, under one lock acquisition. Only Login
    // is carried alongside Generation — never the UserSession reference
    // itself, since that would give a holder indirect access to
    // AccessToken/RefreshToken it has no need for (favorites are scoped by
    // login only).
    public UserSessionSnapshot CaptureSnapshot()
    {
        lock (gate)
        {
            return new UserSessionSnapshot(sessionGeneration, current?.Login);
        }
    }

    // Token-free re-check for a snapshot captured earlier: true only if no
    // SignIn/SignOut has happened since (Generation alone is sufficient —
    // every session change bumps it, including signing back into the same
    // login, so a generation match already implies the login still matches).
    // Callers that awaited a store call after CaptureSnapshot() should call
    // this immediately before applying that call's result to any shared
    // state.
    public bool IsCurrent(UserSessionSnapshot snapshot)
    {
        lock (gate)
        {
            return sessionGeneration == snapshot.Generation;
        }
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

// Generation and Login are always read together from CaptureSnapshot(), so a
// consumer that stashes one of these before an await and compares it against
// UserSessionStore.IsCurrent(...) afterward can detect "the session changed
// while I was awaiting" reliably. Deliberately carries ONLY these two
// non-sensitive values — never the UserSession reference, AccessToken,
// RefreshToken, AvatarUrl, or any token-derived value — since a favorites
// snapshot has no legitimate need for anything beyond "which login" and
// "which generation".
public readonly record struct UserSessionSnapshot(long Generation, string? Login);
