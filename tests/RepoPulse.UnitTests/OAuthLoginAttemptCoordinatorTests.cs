using System.Reflection;
using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

// RP-014: proves OAuthLoginAttemptCoordinator's race semantics directly,
// without needing a real Android Activity/browser to exist — the exact
// races MainActivity/LoginPage must be safe against (a genuine OAuth
// callback losing to a spurious "resumed" signal, a late callback for an
// already-abandoned attempt winning, a screen rotation or cold start
// incorrectly cancelling an in-flight attempt) are reproduced here purely
// through the coordinator's own API.
public class OAuthLoginAttemptCoordinatorTests
{
    [Fact]
    public void BrowserAttemptStarted_PauseThenResumeWithoutCallback_ReturnsToIdle()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        coordinator.StartAttempt();
        coordinator.NotifyPaused();

        var abandoned = coordinator.TryCancelForResumeWithoutCallback();

        Assert.True(abandoned);
        Assert.False(coordinator.HasActiveAttempt);
    }

    [Fact]
    public void ResumeWithoutPriorPause_DoesNotCancelAttempt()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        coordinator.StartAttempt();

        // The system browser never actually took the foreground (no
        // NotifyPaused ever happened) — a resume here must not be treated
        // as "came back from the browser."
        var abandoned = coordinator.TryCancelForResumeWithoutCallback();

        Assert.False(abandoned);
        Assert.True(coordinator.HasActiveAttempt);
    }

    [Fact]
    public void InitialActivityResume_DoesNothing()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();

        // Mirrors MainActivity.OnCreate -> OnResume on a cold start, before
        // any sign-in attempt has ever been started.
        var abandoned = coordinator.TryCancelForResumeWithoutCallback();

        Assert.False(abandoned);
        Assert.False(coordinator.HasActiveAttempt);
    }

    [Fact]
    public void CallbackBeforeResume_CallbackWins()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var attemptId = coordinator.StartAttempt();
        coordinator.NotifyPaused();

        // MainActivity.OnNewIntent always runs before OnResume for a
        // genuine callback (Android's standard singleTop resume order).
        var consumed = coordinator.TryConsumeCallback(attemptId);
        Assert.True(consumed);

        // OnResume's unconditional check must now be a no-op — the
        // callback already won.
        var abandoned = coordinator.TryCancelForResumeWithoutCallback();
        Assert.False(abandoned);
    }

    [Fact]
    public void ResumeBeforeLateCallback_CancelWinsAndLateCallbackIsRejected()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var attemptId = coordinator.StartAttempt();
        coordinator.NotifyPaused();

        var abandoned = coordinator.TryCancelForResumeWithoutCallback();
        Assert.True(abandoned);

        // A callback intent that arrives after the attempt was already
        // declared abandoned (e.g. a delayed/duplicate intent delivery)
        // must never be allowed to proceed.
        var consumed = coordinator.TryConsumeCallback(attemptId);
        Assert.False(consumed);
    }

    [Fact]
    public async Task CallbackAndResumeConcurrent_ExactlyOneTerminalOutcome()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var attemptId = coordinator.StartAttempt();
        coordinator.NotifyPaused();

        var callbackTask = Task.Run(() => coordinator.TryConsumeCallback(attemptId));
        var resumeTask = Task.Run(() => coordinator.TryCancelForResumeWithoutCallback());

        var results = await Task.WhenAll(callbackTask, resumeTask);

        // The coordinator's internal lock guarantees exactly one of these
        // two genuinely racing calls observes itself as the winner —
        // never both, never neither.
        Assert.Single(results, outcome => outcome);
    }

    [Fact]
    public void MultipleResumeEvents_DoNotProduceMultipleOutcomes()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        coordinator.StartAttempt();
        coordinator.NotifyPaused();

        Assert.True(coordinator.TryCancelForResumeWithoutCallback());
        // Further resume signals (e.g. the user backgrounds/foregrounds the
        // already-abandoned page again) must never re-fire the abandonment.
        Assert.False(coordinator.TryCancelForResumeWithoutCallback());
        Assert.False(coordinator.TryCancelForResumeWithoutCallback());
    }

    [Fact]
    public void ScreenRotation_DoesNotResetActiveAttemptIncorrectly()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        coordinator.StartAttempt();

        // MainActivity declares ConfigurationChanges for orientation, so a
        // pure rotation never calls NotifyPaused at all — but even a
        // spurious resume-shaped signal with no matching pause must be
        // safely ignored, repeatedly.
        Assert.False(coordinator.TryCancelForResumeWithoutCallback());
        Assert.False(coordinator.TryCancelForResumeWithoutCallback());
        Assert.True(coordinator.HasActiveAttempt);
    }

    [Fact]
    public void ResumeWithoutCallback_ClearsPendingAuthorizationSession()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var sessionStore = new AuthorizationSessionStore();

        coordinator.StartAttempt();
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));
        coordinator.NotifyPaused();

        var abandoned = coordinator.TryCancelForResumeWithoutCallback();
        Assert.True(abandoned);

        // Mirrors LoginPage.OnAttemptAbandoned's wiring: the coordinator
        // itself never touches AuthorizationSessionStore, but its signal
        // must be sufficient for the caller to safely clear it.
        if (abandoned)
        {
            sessionStore.Reset();
        }

        // Proves the session was genuinely cleared, not just that the
        // coordinator says so: a new attempt can start immediately rather
        // than being rejected as "already in progress."
        Assert.True(sessionStore.TryStart(TimeSpan.FromMinutes(5), out _));
    }

    [Fact]
    public void SuccessfulCallback_StartsTokenExchangeExactlyOnce()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var attemptId = coordinator.StartAttempt();
        coordinator.NotifyPaused();

        Assert.True(coordinator.TryConsumeCallback(attemptId));
        // A duplicate delivery of the same callback intent must never be
        // allowed to start a second token exchange.
        Assert.False(coordinator.TryConsumeCallback(attemptId));
    }

    [Fact]
    public void CancelledAttempt_AllowsImmediateSecondLogin()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var firstAttemptId = coordinator.StartAttempt();
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());

        var secondAttemptId = coordinator.StartAttempt();

        Assert.NotEqual(firstAttemptId, secondAttemptId);
        Assert.True(coordinator.HasActiveAttempt);
    }

    [Fact]
    public void OldCallbackFromCancelledAttempt_DoesNotMatchNewAttempt()
    {
        var coordinator = new OAuthLoginAttemptCoordinator();
        var firstAttemptId = coordinator.StartAttempt();
        coordinator.NotifyPaused();
        Assert.True(coordinator.TryCancelForResumeWithoutCallback());

        var secondAttemptId = coordinator.StartAttempt();

        // A callback intent that was meant for the first (already
        // abandoned) attempt must never be attributed to the second one.
        Assert.False(coordinator.TryConsumeCallback(firstAttemptId));
        Assert.True(coordinator.TryConsumeCallback(secondAttemptId));
    }

    [Fact]
    public void Coordinator_HasNoTokenCodeStateOrVerifierShapedField()
    {
        var fields = typeof(OAuthLoginAttemptCoordinator).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.All(fields, field =>
        {
            Assert.False(field.FieldType == typeof(string), $"Field '{field.Name}' is string-typed and could retain a token/code/state/verifier value.");
            Assert.DoesNotContain("Token", field.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Code", field.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("State", field.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Verifier", field.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Secret", field.Name, StringComparison.OrdinalIgnoreCase);
        });
    }
}
