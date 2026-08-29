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
        // callback the gate must never use as a trigger to fabricate a
        // session that was never actually started.
        var invalidCallback = OAuthCallbackResult.Invalid();

        var decision = OAuthCallbackAttemptGate.Evaluate(
            invalidCallback, sessionStore, coordinator, attemptId, out var session);

        Assert.Equal(OAuthCallbackDecision.AttemptEndedSafely, decision);
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
