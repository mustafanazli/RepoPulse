namespace RepoPulse.Core.Repositories;

// RP-013 hardening: replaces a page-local bool ("isLoadingLatestCommit") +
// a single shared CancellationTokenSource + a single shared "cancelled by
// navigation" bool with an explicit, monotonically-increasing operation
// identity. That trio was correct only as long as exactly one load could
// ever be in flight per page instance — an invariant enforced solely by
// "only one call site ever starts a load", not by any check in the loading
// method itself. This coordinator makes the ownership check explicit and
// provable instead: whichever operation StartOperation() issued LAST is the
// only one allowed to touch shared UI/session state going forward — every
// earlier operation's own continuation (however it resumes, and regardless
// of how many message-loop hops its cancellation takes to be observed) is
// structurally unable to affect anything once IsCurrent(operationId) is
// false. MAUI-independent and carries no token/session data whatsoever —
// only a counter and a CancellationTokenSource.
public sealed class LatestCommitLoadCoordinator
{
    private long currentOperationId;
    private CancellationTokenSource? currentCts;
    private long? navigationCancelledOperationId;

    // True only between StartOperation() and the matching CompleteOperation()
    // for the CURRENTLY active operation — a caller uses this to decide
    // whether a new load attempt should even begin (mirrors the old
    // isLoadingLatestCommit guard, but now centered on operation identity
    // rather than a bare bool that any code path could clear).
    public bool HasActiveOperation => currentCts is not null;

    // Issues a new operation identity strictly greater than any issued
    // before, and a fresh CancellationTokenSource scoped to it. Callers must
    // check HasActiveOperation first — this does not itself refuse to start
    // a second concurrent operation.
    public LatestCommitLoadOperation StartOperation(TimeSpan timeout)
    {
        currentOperationId++;
        var operationId = currentOperationId;
        var cts = new CancellationTokenSource(timeout);
        currentCts = cts;
        return new LatestCommitLoadOperation(operationId, cts.Token);
    }

    // True only for the single most-recently-started operation. Once a newer
    // operation has started, every older operationId permanently returns
    // false here — including for an older operation whose own await/catch/
    // finally is still unwinding. Callers must check this immediately after
    // every await and before every UI/session state mutation.
    public bool IsCurrent(long operationId) => operationId == currentOperationId;

    // Cancels whichever operation is current right now and records that
    // its cancellation was navigation-triggered (as opposed to its own
    // request-timeout firing) — a caller's catch block uses
    // WasCancelledForNavigation(operationId) to tell the two apart without
    // relying on a single shared bool that a later, unrelated operation
    // could stomp on.
    public void CancelForNavigation()
    {
        if (currentCts is null)
        {
            return;
        }

        navigationCancelledOperationId = currentOperationId;
        currentCts.Cancel();
    }

    public bool WasCancelledForNavigation(long operationId) => navigationCancelledOperationId == operationId;

    // Must be called from the operation's own finally block. A no-op for any
    // operationId other than the current one — an older, superseded
    // operation's belated cleanup can never dispose or clear the CTS a
    // newer operation is actively using.
    public void CompleteOperation(long operationId)
    {
        if (operationId != currentOperationId)
        {
            return;
        }

        currentCts?.Dispose();
        currentCts = null;
    }
}

public readonly record struct LatestCommitLoadOperation(long OperationId, CancellationToken Token);
