namespace RepoPulse.Core.Authentication;

// A GitHub OAuth deep-link callback (repopulse://oauth/callback?code=...&state=...)
// carries only what GitHub puts in the redirect — never an
// OAuthLoginAttemptCoordinator attempt id. LoginPage always tells the coordinator
// about its OWN current attempt id (whatever its currentAttemptId field holds at
// the moment the callback arrives) — never one derived from the callback itself,
// because the callback cannot carry one. If an earlier attempt (A) is abandoned
// and a new attempt (B) starts, then A's own real callback arrives late, LoginPage
// has no way to know it belongs to A: it will call the coordinator with B's id.
// Naively marking the coordinator "callback won" before verifying which session
// the callback's state actually matches would let A's stale callback prematurely
// declare B's genuinely still-active attempt terminal.
//
// This gate closes that gap: for a Success outcome, AuthorizationSessionStore
// (the sole source of truth for "does this callback belong to the session we are
// currently waiting on") is consulted FIRST. TryConsume leaves the pending session
// completely untouched on any failure, so a foreign/stale callback can never
// disturb a genuinely active newer attempt. Only once the callback is proven to
// belong to the current session is the coordinator told the callback won.
// Cancelled/Invalid callbacks never carry a state at all (see
// OAuthCallbackParser), so they cannot be validated the same way — the
// coordinator's own terminal gate is the next-best signal there: a stray signal
// after the current attempt already concluded (by any means) becomes a no-op
// instead of disturbing whichever attempt runs next.
//
// Stores nothing itself — no field of any kind — and reads/writes no token,
// authorization code, PKCE state, or code_verifier; it only routes an
// already-parsed OAuthCallbackResult through AuthorizationSessionStore and
// OAuthLoginAttemptCoordinator, both of which already own that data correctly.
public static class OAuthCallbackAttemptGate
{
    public static OAuthCallbackDecision Evaluate(
        OAuthCallbackResult result,
        AuthorizationSessionStore sessionStore,
        OAuthLoginAttemptCoordinator coordinator,
        long currentAttemptId,
        out AuthorizationSession? validatedSession)
    {
        if (result.Outcome == OAuthCallbackOutcome.Success)
        {
            if (sessionStore.TryConsume(result.State, out validatedSession) && validatedSession is not null)
            {
                // Proven to belong to the current session — now, and only now,
                // is it safe to ask the coordinator to let it win. Its own
                // return value must still be honored: a concurrent
                // resume-without-callback may have already won the race on
                // the coordinator's lock (e.g. genuinely simultaneous
                // signals), in which case that path is already the
                // authoritative outcome (OnAttemptAbandoned already resets
                // the session/UI) and this callback — even though its state
                // validated — must not also start an exchange. The session
                // was already consumed above and cannot be un-consumed; that
                // is fine, since a real GitHub callback losing this race is
                // never expected in practice (OnNewIntent always runs before
                // OnResume for a genuine callback) and would otherwise be
                // fully recovered via the abandonment path regardless.
                if (coordinator.TryConsumeCallback(currentAttemptId))
                {
                    return OAuthCallbackDecision.ProceedWithExchange;
                }

                validatedSession = null;
                return OAuthCallbackDecision.Ignored;
            }

            // Wrong/expired/already-used state: never attributable to the
            // current session. Leave the coordinator's active attempt (if any)
            // untouched — whichever attempt is genuinely in flight continues
            // undisturbed, and it will resolve later either via its own real
            // callback or via MainActivity's resume-without-callback signal.
            return OAuthCallbackDecision.Ignored;
        }

        validatedSession = null;

        if (coordinator.TryConsumeCallback(currentAttemptId))
        {
            sessionStore.Reset();
            return OAuthCallbackDecision.AttemptEndedSafely;
        }

        return OAuthCallbackDecision.Ignored;
    }
}

public enum OAuthCallbackDecision
{
    // The callback did not belong to the currently active attempt (wrong/expired
    // state, or the current attempt already concluded by other means) — no
    // session or coordinator state was changed.
    Ignored,

    // The callback's state matched the current session exactly once — the caller
    // must proceed with exactly one token exchange, using the returned session.
    ProceedWithExchange,

    // A Cancelled/Invalid callback genuinely belonging to the current attempt —
    // the pending session was reset; the caller should show a safe message and
    // re-enable sign-in.
    AttemptEndedSafely
}
