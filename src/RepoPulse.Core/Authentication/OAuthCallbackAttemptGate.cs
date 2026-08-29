namespace RepoPulse.Core.Authentication;

// A GitHub OAuth deep-link callback (repopulse://oauth/callback?...) carries
// only what GitHub puts in the redirect — never an OAuthLoginAttemptCoordinator
// attempt id. LoginPage always tells the coordinator about its OWN current
// attempt id (whatever its currentAttemptId field holds at the moment the
// callback arrives) — never one derived from the callback itself, because the
// callback cannot carry one. If an earlier attempt (A) is abandoned and a new
// attempt (B) starts, then A's own real callback arrives late, LoginPage has no
// way to know it belongs to A: it will call the coordinator with B's id.
// Naively marking the coordinator "callback won" (or resetting the pending
// session) before verifying which session the callback's state actually
// matches would let A's stale callback prematurely end B's genuinely still-
// active attempt — this holds for every outcome, not only Success.
//
// AuthorizationSessionStore.TryConsume is the sole source of truth for "does
// this callback belong to the session we are currently waiting on" — it
// leaves the pending session completely untouched on any failure, so a
// foreign/stale callback (of any outcome) can never disturb a genuinely
// active newer attempt. Only once a callback's state is proven to match the
// current session is the coordinator ever told the callback won:
//
//   - Success: code + state required; state must match; only then can the
//     coordinator go terminal; the caller proceeds with exactly one exchange.
//   - Cancelled (access_denied) / other OAuth error: GitHub echoes back
//     whatever state we sent when the original request included one (RFC 6749
//     4.1.2.1) — see OAuthCallbackParser. If a state is present, it MUST match
//     the current session before this is allowed to end the current attempt.
//     A callback with no state, or a state that does not match, is completely
//     ignored: it can neither cancel nor reset whatever attempt is genuinely
//     active. Raw error/error_description values are never read here and
//     never reach the caller via this type's return values.
//   - Invalid (missing/malformed code or state, unparsable callback): can
//     never be validated the same way when no state is present or it does
//     not match — always Ignored. The callback path itself must never declare
//     an unvalidated attempt abandoned; that is deliberately left to
//     MainActivity's own resume-without-callback signal (a pure Activity
//     lifecycle fact, independent of callback content), which fires
//     separately and safely handles this app-still-alive-but-nothing-usable-
//     arrived case.
//
// Stores nothing itself — no field of any kind — and reads/writes no token,
// authorization code, PKCE state, or code_verifier; it only routes an
// already-parsed OAuthCallbackResult through AuthorizationSessionStore and
// OAuthLoginAttemptCoordinator, both of which already own that data correctly.
// No new AuthorizationSessionStore API was needed: TryConsume already both
// validates and safely (single-use) consumes a state without ever exposing a
// token, which is exactly what every outcome below needs.
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
                return TryGoTerminal(coordinator, currentAttemptId, OAuthCallbackDecision.ProceedWithExchange, ref validatedSession);
            }

            // Wrong/expired/already-used/missing state: never attributable to
            // the current session. Leave the coordinator's active attempt (if
            // any) untouched — whichever attempt is genuinely in flight
            // continues undisturbed.
            return OAuthCallbackDecision.Ignored;
        }

        validatedSession = null;

        // Cancelled / Invalid: only a state that genuinely matches the
        // current session may end the current attempt. A callback with no
        // state at all (string.IsNullOrEmpty short-circuits inside TryConsume
        // itself) or a foreign/mismatched one is always Ignored — it must
        // never cancel or reset whatever attempt is genuinely active.
        if (!sessionStore.TryConsume(result.State, out _))
        {
            return OAuthCallbackDecision.Ignored;
        }

        return TryGoTerminal(coordinator, currentAttemptId, OAuthCallbackDecision.AttemptEndedSafely, ref validatedSession);
    }

    // Common tail for both branches above, once AuthorizationSessionStore has
    // already proven the callback belongs to the current session: the
    // coordinator's own return value must still be honored, since a
    // concurrent resume-without-callback may have already won the race on
    // the coordinator's separate lock (the session and coordinator are two
    // distinct locks, so this ordering — validate session, then ask the
    // coordinator — is the only point where the two are reconciled). If the
    // coordinator says the attempt already concluded by another path, that
    // path (OnAttemptAbandoned) is already authoritative and this call must
    // not also act — even though the session was already consumed above and
    // cannot be un-consumed, which is fine: a real callback losing this race
    // is not expected in practice (OnNewIntent always runs before OnResume
    // for a genuine callback) and is fully recovered via the abandonment path
    // regardless.
    private static OAuthCallbackDecision TryGoTerminal(
        OAuthLoginAttemptCoordinator coordinator,
        long currentAttemptId,
        OAuthCallbackDecision onSuccess,
        ref AuthorizationSession? validatedSession)
    {
        if (coordinator.TryConsumeCallback(currentAttemptId))
        {
            return onSuccess;
        }

        validatedSession = null;
        return OAuthCallbackDecision.Ignored;
    }
}

public enum OAuthCallbackDecision
{
    // The callback did not belong to the currently active attempt (wrong/
    // expired/missing state, or the current attempt already concluded by
    // other means) — no session or coordinator state was changed.
    Ignored,

    // The callback's state matched the current session exactly once — the
    // caller must proceed with exactly one token exchange, using the
    // returned session.
    ProceedWithExchange,

    // A Cancelled/Invalid callback whose state genuinely matched the current
    // session — the pending session was consumed; the caller should show a
    // safe message and re-enable sign-in.
    AttemptEndedSafely
}
