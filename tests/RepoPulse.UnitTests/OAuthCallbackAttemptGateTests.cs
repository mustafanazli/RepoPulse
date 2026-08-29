using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

// RP-014 follow-up audit: OAuthLoginAttemptCoordinatorTests.cs proved the
// coordinator's own race semantics in isolation, but its
// OldCallbackFromCancelledAttempt_DoesNotMatchNewAttempt test passed the OLD
// attempt id directly into TryConsumeCallback — that does NOT model the real
// Android/LoginPage integration. In production, a callback carries only
// code/state/error (see OAuthCallbackParser) — never an attempt id — so
// LoginPage always calls the coordinator with its OWN current field, whatever
// that happens to be at the moment the callback arrives, never one derived
// from the callback. These tests exercise OAuthCallbackAttemptGate exactly
// the way LoginPage does: currentAttemptId is always the CALLER's current
// context, and a stale callback is distinguished only via
// AuthorizationSessionStore's own state validation — never via an attempt id
// the callback cannot actually carry.
public class OAuthCallbackAttemptGateTests
{
    [Fact]
    public void AbandonedAttemptA_ThenAttemptB_OldStateACallbackUsingCurrentAttemptContext_NeverStartsExchange()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        // Attempt A starts and gets its own PKCE state.
        coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionA));

        // A is abandoned exactly like MainActivity.OnResume -> OnAttemptAbandoned.
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        sessionStore.Reset();

        // Attempt B starts with a brand-new state.
        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));

        // A's own real callback (correct code+state for A) arrives late.
        // Production LoginPage has no way to know this belongs to A — it
        // always passes its own *current* field, which by now holds
        // attemptIdB, never attemptIdA.
        var staleCallbackFromA = OAuthCallbackResult.Success("code-for-a", sessionA.State);

        var decision = OAuthCallbackAttemptGate.Evaluate(
            staleCallbackFromA, sessionStore, coordinator, attemptIdB, out var validatedSession);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(validatedSession);
    }

    [Fact]
    public void OldStateACallback_DoesNotBecomeSuccessfulCallbackForAttemptB()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionA));
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        sessionStore.Reset();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));

        var staleCallbackFromA = OAuthCallbackResult.Success("code-for-a", sessionA.State);
        OAuthCallbackAttemptGate.Evaluate(staleCallbackFromA, sessionStore, coordinator, attemptIdB, out _);

        // B's own attempt must still be genuinely active — the stale A
        // callback must never have been mistaken for B's successful callback.
        Assert.True(coordinator.HasActiveAttempt);
    }

    [Fact]
    public void InvalidOldCallback_LeavesUiRecoverableAndAllowsAttemptC()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdA = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        sessionStore.Reset();

        // Cancelled/Invalid callbacks never carry a state (see
        // OAuthCallbackParser), so a stray one arriving after A already
        // concluded cannot be positively attributed to anything — it must be
        // a safe no-op, never a fresh source of stuck state.
        var staleInvalidCallback = OAuthCallbackResult.Invalid();
        var decision = OAuthCallbackAttemptGate.Evaluate(
            staleInvalidCallback, sessionStore, coordinator, attemptIdA, out _);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.False(coordinator.HasActiveAttempt);

        // A brand-new attempt C must be immediately startable — no half or
        // locked state left behind.
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));
        var attemptIdC = coordinator.StartAttempt();
        Assert.True(coordinator.HasActiveAttempt);
        Assert.NotEqual(attemptIdA, attemptIdC);
    }

    [Fact]
    public void ValidStateBCallback_StartsExchangeExactlyOnce()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        var validCallback = OAuthCallbackResult.Success("code-for-b", sessionB.State);

        var firstDecision = OAuthCallbackAttemptGate.Evaluate(
            validCallback, sessionStore, coordinator, attemptIdB, out var firstSession);
        Assert.Equal(OAuthCallbackDecision.ProceedWithExchange, firstDecision);
        Assert.NotNull(firstSession);

        // A duplicate delivery of the exact same callback must never be
        // allowed to start a second token exchange.
        var secondDecision = OAuthCallbackAttemptGate.Evaluate(
            validCallback, sessionStore, coordinator, attemptIdB, out var secondSession);
        Assert.Equal(OAuthCallbackDecision.Ignored, secondDecision);
        Assert.Null(secondSession);
    }

    [Fact]
    public void OldCallbackThenValidCurrentCallback_BehaviorIsExplicitAndRaceSafe()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionA));
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        sessionStore.Reset();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // Old A callback arrives first, using the current (B) context, exactly
        // as production always does.
        var oldCallback = OAuthCallbackResult.Success("code-for-a", sessionA.State);
        var oldDecision = OAuthCallbackAttemptGate.Evaluate(oldCallback, sessionStore, coordinator, attemptIdB, out _);
        Assert.Equal(OAuthCallbackDecision.Ignored, oldDecision);

        // B's genuine callback then arrives and must succeed exactly once.
        var realCallback = OAuthCallbackResult.Success("code-for-b", sessionB.State);
        var realDecision = OAuthCallbackAttemptGate.Evaluate(
            realCallback, sessionStore, coordinator, attemptIdB, out var validatedSession);
        Assert.Equal(OAuthCallbackDecision.ProceedWithExchange, realDecision);
        Assert.Equal(sessionB.State, validatedSession!.State);
    }

    [Fact]
    public void CallbackValidationAndCoordinatorTerminalTransition_HaveCorrectOrdering()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var session));

        // A callback with the wrong state must fail validation WITHOUT ever
        // marking the coordinator's genuinely active attempt terminal —
        // validation must run strictly before any terminal transition.
        var wrongStateCallback = OAuthCallbackResult.Success("some-code", "not-the-real-state");
        var decision = OAuthCallbackAttemptGate.Evaluate(
            wrongStateCallback, sessionStore, coordinator, attemptId, out _);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.True(coordinator.HasActiveAttempt);

        // The real callback for this exact attempt must still succeed
        // afterwards — the failed validation above left everything intact.
        var realCallback = OAuthCallbackResult.Success("real-code", session.State);
        var realDecision = OAuthCallbackAttemptGate.Evaluate(
            realCallback, sessionStore, coordinator, attemptId, out var validatedSession);
        Assert.Equal(OAuthCallbackDecision.ProceedWithExchange, realDecision);
        Assert.NotNull(validatedSession);
    }

    [Fact]
    public async Task ConcurrentValidCallbackAndResume_ExactlyOneTerminalOutcome()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var session));
        coordinator.NotifyPaused();

        var validCallback = OAuthCallbackResult.Success("real-code", session.State);

        var callbackTask = Task.Run(() =>
        {
            var decision = OAuthCallbackAttemptGate.Evaluate(validCallback, sessionStore, coordinator, attemptId, out _);
            return decision == OAuthCallbackDecision.ProceedWithExchange;
        });
        var resumeTask = Task.Run(() => coordinator.TryCancelForResumeWithoutCallback());

        var results = await Task.WhenAll(callbackTask, resumeTask);

        Assert.Single(results, outcome => outcome);
    }

    [Fact]
    public void InvalidCallbackNeverCreatesOrPersistsSession()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        // No sessionStore.TryStart call here — this models an Invalid
        // callback (no state at all) the gate must never use as a trigger to
        // fabricate or touch a session that was never actually started.
        var invalidCallback = OAuthCallbackResult.Invalid();

        var decision = OAuthCallbackAttemptGate.Evaluate(
            invalidCallback, sessionStore, coordinator, attemptId, out var session);

        // A callback with no state to validate can never be attributed to
        // any session — always Ignored, never treated as this attempt's own
        // cancellation.
        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(session);
        Assert.False(sessionStore.TryConsume("anything", out _));
    }

    [Fact]
    public void OAuthCallbackAttemptGate_HasNoTokenCodeStateOrVerifierShapedField()
    {
        // Fully static and stateless — it only routes calls to
        // AuthorizationSessionStore/OAuthLoginAttemptCoordinator, both of
        // which already own token/code/state/verifier data correctly.
        var fields = typeof(OAuthCallbackAttemptGate).GetFields(
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Instance);

        Assert.Empty(fields);
    }

    [Fact]
    public void ProductionCallbackPayload_CarriesNoAttemptId()
    {
        // OAuthCallbackResult is the exact type MainActivity/OAuthCallbackBroker
        // deliver to LoginPage in production — built solely from the raw
        // callback URI (code/state/error/error_description). It structurally
        // cannot carry an OAuthLoginAttemptCoordinator attempt id, which is
        // why every test above passes currentAttemptId as an independent
        // parameter (mirroring LoginPage's own field) rather than deriving it
        // from the callback result itself — anything else would not model
        // the real Android/broker integration.
        var properties = typeof(OAuthCallbackResult).GetProperties();
        Assert.All(properties, property =>
            Assert.DoesNotContain("Attempt", property.Name, StringComparison.OrdinalIgnoreCase));
    }
}

// RP-014 follow-up audit #2: the first gate fix correctly reordered the
// Success outcome (validate state, then go terminal) but left Cancelled/
// Invalid unconditionally ending whatever attempt the coordinator currently
// considers active — with no state check at all, since OAuthCallbackParser
// used to discard state for those outcomes. A stale Cancelled/Invalid signal
// from an already-abandoned attempt A, arriving while a genuinely active
// attempt B is in flight, could therefore wipe B's still-valid pending
// session purely because "some attempt is currently active" — never
// verifying that the signal actually belongs to B. These tests exercise the
// corrected behavior: every outcome now requires AuthorizationSessionStore to
// confirm the callback's state (when present) matches the current session
// before it may end that attempt — exactly like Success — and none of them
// inject an artificial callback attemptId (item #14): currentAttemptId is
// always passed as the caller's own current context, mirroring LoginPage's
// real field, never derived from the callback (see
// ProductionCallbackPayload_CarriesNoAttemptId above, which proves
// OAuthCallbackResult cannot carry one regardless of outcome).
public class OAuthCallbackOutcomeValidationTests
{
    [Fact]
    public void OldAccessDeniedForA_WhileBActive_DoesNotCancelOrResetB()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionA));
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        sessionStore.Reset();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // GitHub's own "user cancelled" redirect for A, echoing A's real
        // state, arrives late while B is active.
        var oldAccessDenied = OAuthCallbackResult.Cancelled("access_denied", "The user cancelled", sessionA.State);

        var decision = OAuthCallbackAttemptGate.Evaluate(oldAccessDenied, sessionStore, coordinator, attemptIdB, out var session);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(session);
        Assert.True(coordinator.HasActiveAttempt);
        // B's own session is still exactly the one issued for B.
        Assert.True(sessionStore.TryConsume(sessionB.State, out var consumed));
        Assert.NotNull(consumed);
    }

    [Fact]
    public void OldOAuthErrorForA_WhileBActive_DoesNotCancelOrResetB()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionA));
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        sessionStore.Reset();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // A non-access_denied OAuth error (e.g. server_error) for A, echoing
        // A's real state, arrives late while B is active.
        var oldServerError = OAuthCallbackResult.Invalid("server_error", "Something went wrong", sessionA.State);

        var decision = OAuthCallbackAttemptGate.Evaluate(oldServerError, sessionStore, coordinator, attemptIdB, out var session);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(session);
        Assert.True(coordinator.HasActiveAttempt);
        Assert.True(sessionStore.TryConsume(sessionB.State, out _));
    }

    [Fact]
    public void MissingStateCallback_WhileBActive_DoesNotConsumeB()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // Models "code present, state missing" — OAuthCallbackParser itself
        // classifies this as Invalid with no state to validate.
        var missingState = OAuthCallbackResult.Invalid();

        var decision = OAuthCallbackAttemptGate.Evaluate(missingState, sessionStore, coordinator, attemptIdB, out var session);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(session);
        Assert.True(coordinator.HasActiveAttempt);
        Assert.True(sessionStore.TryConsume(sessionB.State, out _));
    }

    [Fact]
    public void MismatchedStateCallback_WhileBActive_DoesNotConsumeB()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // An OAuth error carrying some state that is neither B's real state
        // nor derived from any real prior attempt — an arbitrary/foreign
        // value (e.g. a forged or corrupted deep link).
        var mismatched = OAuthCallbackResult.Invalid("temporarily_unavailable", null, "completely-unrelated-state");

        var decision = OAuthCallbackAttemptGate.Evaluate(mismatched, sessionStore, coordinator, attemptIdB, out var session);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(session);
        Assert.True(coordinator.HasActiveAttempt);
        Assert.True(sessionStore.TryConsume(sessionB.State, out _));
    }

    [Fact]
    public void MalformedCallback_WhileBActive_DoesNotConsumeB()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // Totally malformed/unparseable callback (e.g. unrecognized scheme/
        // host/path, or an undecodable query) — OAuthCallbackParser always
        // classifies this as a bare Invalid() with no error and no state.
        var malformed = OAuthCallbackResult.Invalid();

        var decision = OAuthCallbackAttemptGate.Evaluate(malformed, sessionStore, coordinator, attemptIdB, out var session);

        Assert.Equal(OAuthCallbackDecision.Ignored, decision);
        Assert.Null(session);
        Assert.True(coordinator.HasActiveAttempt);
        Assert.True(sessionStore.TryConsume(sessionB.State, out _));
    }

    [Fact]
    public void ValidAccessDeniedForB_CancelsBAndClearsItsSession()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        var validCancel = OAuthCallbackResult.Cancelled("access_denied", "The user cancelled", sessionB.State);

        var decision = OAuthCallbackAttemptGate.Evaluate(validCancel, sessionStore, coordinator, attemptIdB, out var session);

        Assert.Equal(OAuthCallbackDecision.AttemptEndedSafely, decision);
        Assert.Null(session);
        Assert.False(coordinator.HasActiveAttempt);
        // Session was genuinely cleared — a fresh attempt can start immediately.
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));
    }

    [Fact]
    public void ValidOAuthErrorForB_TerminatesExactlyOnce()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        var validError = OAuthCallbackResult.Invalid("temporarily_unavailable", null, sessionB.State);

        var firstDecision = OAuthCallbackAttemptGate.Evaluate(validError, sessionStore, coordinator, attemptIdB, out _);
        Assert.Equal(OAuthCallbackDecision.AttemptEndedSafely, firstDecision);

        // A duplicate delivery of the exact same error callback must never
        // terminate anything a second time.
        var secondDecision = OAuthCallbackAttemptGate.Evaluate(validError, sessionStore, coordinator, attemptIdB, out _);
        Assert.Equal(OAuthCallbackDecision.Ignored, secondDecision);
    }

    [Fact]
    public void OldInvalidCallbackThenValidBCallback_BStillSucceedsExactlyOnce()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        // A stale/foreign invalid signal (no state, or a state that doesn't
        // match B) arrives first.
        var staleInvalid = OAuthCallbackResult.Invalid("server_error", null, "some-other-attempts-state");
        var staleDecision = OAuthCallbackAttemptGate.Evaluate(staleInvalid, sessionStore, coordinator, attemptIdB, out _);
        Assert.Equal(OAuthCallbackDecision.Ignored, staleDecision);

        // B's genuine callback must still succeed afterwards, exactly once.
        var validCallback = OAuthCallbackResult.Success("real-code", sessionB.State);
        var decision = OAuthCallbackAttemptGate.Evaluate(validCallback, sessionStore, coordinator, attemptIdB, out var session);
        Assert.Equal(OAuthCallbackDecision.ProceedWithExchange, decision);
        Assert.NotNull(session);
    }

    [Fact]
    public async Task ConcurrentValidCancellationAndResume_ExactlyOneTerminalOutcome()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var session));
        coordinator.NotifyPaused();

        var validCancel = OAuthCallbackResult.Cancelled("access_denied", null, session.State);

        var cancelTask = Task.Run(() =>
        {
            var decision = OAuthCallbackAttemptGate.Evaluate(validCancel, sessionStore, coordinator, attemptId, out _);
            return decision == OAuthCallbackDecision.AttemptEndedSafely;
        });
        var resumeTask = Task.Run(() => coordinator.TryCancelForResumeWithoutCallback());

        var results = await Task.WhenAll(cancelTask, resumeTask);

        Assert.Single(results, outcome => outcome);
    }

    [Fact]
    public async Task ConcurrentValidSuccessAndCancellation_ExactlyOneExchangeOrCancellation()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var session));

        var validSuccess = OAuthCallbackResult.Success("real-code", session.State);
        var validCancel = OAuthCallbackResult.Cancelled("access_denied", null, session.State);

        var successTask = Task.Run(() =>
            OAuthCallbackAttemptGate.Evaluate(validSuccess, sessionStore, coordinator, attemptId, out _));
        var cancelTask = Task.Run(() =>
            OAuthCallbackAttemptGate.Evaluate(validCancel, sessionStore, coordinator, attemptId, out _));

        var decisions = await Task.WhenAll(successTask, cancelTask);

        // AuthorizationSessionStore's own single-use consumption guarantees
        // exactly one of these two genuinely racing, same-state callbacks
        // can ever be treated as authoritative — the other must be Ignored.
        Assert.Single(decisions, d => d is OAuthCallbackDecision.ProceedWithExchange or OAuthCallbackDecision.AttemptEndedSafely);
        Assert.Single(decisions, d => d == OAuthCallbackDecision.Ignored);
    }

    [Fact]
    public void InvalidCallbackNeverStartsExchange()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var session));

        OAuthCallbackResult[] invalidVariants =
        [
            OAuthCallbackResult.Invalid(),
            OAuthCallbackResult.Invalid("server_error", "oops"),
            OAuthCallbackResult.Invalid("server_error", "oops", "wrong-state"),
            OAuthCallbackResult.Cancelled("access_denied", null),
            OAuthCallbackResult.Cancelled("access_denied", null, "wrong-state"),
        ];

        foreach (var invalid in invalidVariants)
        {
            var decision = OAuthCallbackAttemptGate.Evaluate(invalid, sessionStore, coordinator, attemptId, out var validated);
            Assert.NotEqual(OAuthCallbackDecision.ProceedWithExchange, decision);
            Assert.Null(validated);
        }

        // The genuinely pending session for this attempt was never touched
        // by any of the above.
        Assert.True(sessionStore.TryConsume(session.State, out _));
    }

    [Fact]
    public void InvalidCallbackNeverClearsAnotherAttemptSession()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptIdB = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out var sessionB));

        var invalidForSomeoneElse = OAuthCallbackResult.Invalid("access_denied_variant", null, "not-b-state");
        OAuthCallbackAttemptGate.Evaluate(invalidForSomeoneElse, sessionStore, coordinator, attemptIdB, out _);

        // B's session must still be fully usable afterwards.
        Assert.True(sessionStore.TryConsume(sessionB.State, out var consumed));
        Assert.NotNull(consumed);
    }

    [Fact]
    public void RawErrorAndStateNeverReachDecisionOrSession()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        var attemptId = coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));

        const string secretMarker = "SECRET-ERROR-DESCRIPTION-MARKER-DO-NOT-LEAK";
        const string stateMarker = "SECRET-STATE-MARKER-DO-NOT-LEAK";
        var callback = OAuthCallbackResult.Invalid("server_error", secretMarker, stateMarker);

        var decision = OAuthCallbackAttemptGate.Evaluate(callback, sessionStore, coordinator, attemptId, out var session);

        // The gate's entire return surface is a 3-value enum plus an
        // AuthorizationSession (State/CodeVerifier/CodeChallenge/
        // ExpiresAtUtc only, see AuthorizationSession.cs) — neither can carry
        // arbitrary error/error_description/state text through to a caller,
        // log, or UI.
        Assert.Null(session);
        Assert.DoesNotContain(secretMarker, decision.ToString());
        Assert.DoesNotContain(stateMarker, decision.ToString());
    }
}
