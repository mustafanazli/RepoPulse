using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// RP-013 hardening: proves LatestCommitLoadCoordinator's ownership
// guarantees directly, without needing a real MAUI page/OnAppearing to
// exist — the exact races RepositoryDetailPage.xaml.cs must be safe against
// (a superseded operation's belated continuation clearing loading state,
// cancelling a newer token, or overwriting a newer result) are reproduced
// here purely through the coordinator's own API.
public class LatestCommitLoadCoordinatorTests
{
    [Fact]
    public void StartOperation_IssuesStrictlyIncreasingOperationIds()
    {
        var coordinator = new LatestCommitLoadCoordinator();

        var first = coordinator.StartOperation(TimeSpan.FromSeconds(15));
        coordinator.CompleteOperation(first.OperationId);
        var second = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        Assert.NotEqual(first.OperationId, second.OperationId);
        Assert.True(second.OperationId > first.OperationId);
    }

    [Fact]
    public void OldRequestFinally_AfterNewRequestStarts_DoesNotClearNewLoadingState()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var oldOperation = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        // The old operation's own await is still unwinding — its finally
        // has not run yet — when a new operation starts (e.g. a fast
        // re-appear resumes loading before the old one's cancellation
        // continuation has been scheduled).
        var newOperation = coordinator.StartOperation(TimeSpan.FromSeconds(15));
        Assert.True(coordinator.HasActiveOperation);
        Assert.True(coordinator.IsCurrent(newOperation.OperationId));

        // The old operation's finally now runs, belatedly.
        coordinator.CompleteOperation(oldOperation.OperationId);

        // The new operation's loading state must still read as active —
        // the old operation's cleanup must be a complete no-op.
        Assert.True(coordinator.HasActiveOperation);
        Assert.True(coordinator.IsCurrent(newOperation.OperationId));
    }

    [Fact]
    public void OldCancelledRequest_DoesNotOverwriteNewResult()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var oldOperation = coordinator.StartOperation(TimeSpan.FromSeconds(15));
        coordinator.CancelForNavigation();

        var newOperation = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        // The new operation is free to apply its own result...
        Assert.True(coordinator.IsCurrent(newOperation.OperationId));

        // ...while the old operation's own (only now resuming) continuation
        // must see itself as stale and therefore never apply its outcome.
        Assert.False(coordinator.IsCurrent(oldOperation.OperationId));
    }

    [Fact]
    public void OnDisappearingCancellation_DoesNotShowError()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var operation = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        coordinator.CancelForNavigation();

        Assert.True(operation.Token.IsCancellationRequested);
        // This is exactly the check RepositoryDetailPage's catch
        // (OperationCanceledException) block makes before ever calling
        // SetLatestCommitError — true here means the page must stay silent.
        Assert.True(coordinator.WasCancelledForNavigation(operation.OperationId));
    }

    [Fact]
    public void TimeoutCancellation_IsNotMisreportedAsNavigationCancellation()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var operation = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        // No CancelForNavigation() call — only a genuine request timeout
        // would cancel this token, which the page must still report as an
        // error rather than silently swallow.
        Assert.False(coordinator.WasCancelledForNavigation(operation.OperationId));
    }

    [Fact]
    public void ConcurrentOnAppearing_StartsAtMostOneActiveRequest()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var startedOperationIds = new List<long>();

        // Mirrors RepositoryDetailPage.LoadLatestCommitAsync's own guard:
        // check HasActiveOperation before starting.
        void SimulateLoadAttempt()
        {
            if (coordinator.HasActiveOperation)
            {
                return;
            }

            startedOperationIds.Add(coordinator.StartOperation(TimeSpan.FromSeconds(15)).OperationId);
        }

        // Three load attempts in immediate succession (e.g. OnAppearing
        // firing more than once in a fast resume) while the first is still
        // in flight.
        SimulateLoadAttempt();
        SimulateLoadAttempt();
        SimulateLoadAttempt();

        Assert.Single(startedOperationIds);
    }

    [Fact]
    public void CompleteOperation_ForCurrentOperation_ClearsActiveState()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var operation = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        coordinator.CompleteOperation(operation.OperationId);

        Assert.False(coordinator.HasActiveOperation);
    }

    [Fact]
    public void CompleteOperation_ForStaleOperationId_IsANoOp()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var oldOperation = coordinator.StartOperation(TimeSpan.FromSeconds(15));
        coordinator.CompleteOperation(oldOperation.OperationId);
        var newOperation = coordinator.StartOperation(TimeSpan.FromSeconds(15));

        // A duplicate/late completion call for the already-finished old
        // operation must never touch the new operation's active state.
        coordinator.CompleteOperation(oldOperation.OperationId);

        Assert.True(coordinator.HasActiveOperation);
        Assert.True(coordinator.IsCurrent(newOperation.OperationId));
    }

    // Proves the coordinator's operation identity and UserSessionStore's
    // session identity are two INDEPENDENT discard signals — a page must
    // check both, since a session change with no new load started (this
    // test) is invisible to the coordinator alone, exactly as a new load
    // with no session change is invisible to UserSessionStore alone.
    [Fact]
    public void SessionChanged_StillDiscardsLateResult()
    {
        var coordinator = new LatestCommitLoadCoordinator();
        var operation = coordinator.StartOperation(TimeSpan.FromSeconds(15));
        var userSessionStore = new UserSessionStore();
        userSessionStore.SignIn(new UserSession("token-a", null, "alice", null));
        var sessionSnapshot = userSessionStore.CaptureSnapshot();

        // No new commit load starts, but the session changes while this
        // one is still in flight.
        userSessionStore.SignOut();
        userSessionStore.SignIn(new UserSession("token-b", null, "bob", null));

        Assert.True(coordinator.IsCurrent(operation.OperationId));
        Assert.False(userSessionStore.IsCurrent(sessionSnapshot));
    }
}
