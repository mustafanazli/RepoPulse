using RepoPulse.Core.Authentication;

namespace RepoPulse.Authentication;

// Minimal pub/sub so MainActivity (which owns the Android intent-filter) can hand
// parsed OAuth callback results to the MAUI UI layer without a DI container.
// Temporary for the RP-002 dev-verification screen; replace when the real
// Faz 1 login flow consumes this instead of MainPage.
//
// Delivery is exactly-once: if a page is already subscribed when Publish runs,
// the result is delivered live and nothing is retained. If nobody is subscribed
// yet (cold start races MainPage's own startup), the result is queued and handed
// to the first page that calls TryConsumePendingResult, then discarded — so a
// later page instance never re-shows an old callback.
public static class OAuthCallbackBroker
{
    private static readonly object Gate = new();
    private static OAuthCallbackResult? _pendingResult;

    public static event EventHandler<OAuthCallbackResult>? CallbackReceived;

    // RP-014: MAUI-independent, unit-tested (RepoPulse.Core.Authentication)
    // coordinator that resolves the race between MainActivity's raw Android
    // Activity lifecycle and LoginPage's OAuth flow — see its own doc
    // comment. A single shared instance, exactly like this broker itself,
    // since MainActivity (a static context) and LoginPage both need to
    // reach the same attempt state.
    public static readonly OAuthLoginAttemptCoordinator AttemptCoordinator = new();

    // Raised when MainActivity.OnResume observes that a sign-in attempt was
    // abandoned (paused for the system browser, resumed with no callback
    // ever received). Carries no data — a subscriber reacts by resetting
    // its own UI state and any pending authorization session.
    public static event Action? AttemptAbandoned;

    public static void PublishAttemptAbandoned() => AttemptAbandoned?.Invoke();

    public static void Publish(OAuthCallbackResult result)
    {
        var handler = CallbackReceived;
        if (handler is not null)
        {
            handler.Invoke(null, result);
            return;
        }

        lock (Gate)
        {
            _pendingResult = result;
        }
    }

    public static OAuthCallbackResult? TryConsumePendingResult()
    {
        lock (Gate)
        {
            var result = _pendingResult;
            _pendingResult = null;
            return result;
        }
    }
}
