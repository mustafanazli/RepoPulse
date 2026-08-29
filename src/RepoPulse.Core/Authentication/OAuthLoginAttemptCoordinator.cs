namespace RepoPulse.Core.Authentication;

// RP-014 fix: resolves the race between MainActivity's raw Android Activity
// lifecycle (OnPause/OnResume — which fire for many reasons unrelated to
// login: home button, notification shade, recent apps, an unrelated dialog)
// and LoginPage's OAuth flow. Before this existed, a sign-in attempt whose
// system-browser tab never delivered a callback intent (offline device, or
// the user simply backing out) left LoginPage's sign-in button disabled
// forever — nothing ever called EndSignInAttempt(). This coordinator lets
// MainActivity report "paused" / "resumed with no callback seen" without
// itself knowing anything about login UI, and lets LoginPage learn "this
// attempt was abandoned, reset the page" without depending on Activity
// lifecycle plumbing directly.
//
// Deliberately holds ONLY a monotonic attempt id and booleans — never a
// token, authorization code, PKCE state, or code_verifier. Clearing the
// actual pending AuthorizationSession is the caller's responsibility once
// TryCancelForResumeWithoutCallback() returns true.
//
// No timers/timestamps are used anywhere here — every decision is driven
// purely by the order in which StartAttempt/NotifyPaused/TryConsumeCallback/
// TryCancelForResumeWithoutCallback are actually called, so behavior is
// deterministic and fully testable without waiting on real time.
public sealed class OAuthLoginAttemptCoordinator
{
    private readonly object gate = new();
    private long currentAttemptId;
    private bool isTerminal;
    private bool hasPausedSinceAttemptStart;

    // True only for the current attempt, between StartAttempt() and whichever
    // of TryConsumeCallback()/TryCancelForResumeWithoutCallback() first wins.
    public bool HasActiveAttempt
    {
        get
        {
            lock (gate)
            {
                return currentAttemptId != 0 && !isTerminal;
            }
        }
    }

    // Called right before the system browser is launched for a new sign-in
    // attempt. Always succeeds and always issues a strictly-increasing id —
    // a previous attempt (if any) is implicitly superseded; its id can never
    // win a future TryConsumeCallback/TryCancelForResumeWithoutCallback call.
    public long StartAttempt()
    {
        lock (gate)
        {
            currentAttemptId++;
            isTerminal = false;
            hasPausedSinceAttemptStart = false;
            return currentAttemptId;
        }
    }

    // Called from MainActivity.OnPause on every pause, for any reason. Safe
    // to call with no attempt active — it only ever affects a subsequent
    // TryCancelForResumeWithoutCallback() call.
    public void NotifyPaused()
    {
        lock (gate)
        {
            hasPausedSinceAttemptStart = true;
        }
    }

    // Called from MainActivity.OnResume on every resume, for any reason —
    // including the very first resume after a cold start, and including a
    // resume that follows a genuine OAuth callback. A no-op unless there is
    // a current, non-terminal attempt that has actually been paused since
    // it started: that combination is what distinguishes "the system
    // browser really did take the foreground and we're now back without
    // ever hearing from it" from a cold start, an unrelated resume with no
    // login in flight, or a resume that immediately follows
    // TryConsumeCallback() already having won for this attempt (screen
    // rotation never reaches here at all — MainActivity declares
    // ConfigurationChanges for orientation, so Android never tears down/
    // resumes the Activity for a pure rotation). Returns true only for the
    // single call that abandons the attempt; the caller must then reset its
    // own UI state and clear any pending authorization session.
    public bool TryCancelForResumeWithoutCallback()
    {
        lock (gate)
        {
            if (currentAttemptId == 0 || isTerminal || !hasPausedSinceAttemptStart)
            {
                return false;
            }

            isTerminal = true;
            return true;
        }
    }

    // Called when a real OAuth callback (success, cancelled, or invalid —
    // any classification) is about to be processed for attemptId. Returns
    // true only if attemptId is still the current, non-terminal attempt —
    // false for a callback belonging to an already-abandoned or superseded
    // attempt, which must never be allowed to start a token exchange or
    // otherwise reach the UI.
    public bool TryConsumeCallback(long attemptId)
    {
        lock (gate)
        {
            if (currentAttemptId == 0 || isTerminal || attemptId != currentAttemptId)
            {
                return false;
            }

            isTerminal = true;
            return true;
        }
    }
}
