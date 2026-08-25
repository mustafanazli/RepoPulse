using RepoPulse.Core.Authentication;

namespace RepoPulse.Authentication;

// Non-sensitive, boolean fallback marker backed by MAUI Preferences —
// deliberately NOT SecureStorage: it holds no secret, and a Preferences
// failure is a near-disjoint failure mode from the Android
// Keystore/backup-restore issue this marker exists to cover for (see
// MauiSecureSessionStorage / SessionPersistenceStore). Touches exactly this
// one key.
public sealed class MauiSessionInvalidationMarker : ISessionInvalidationMarker
{
    public const string MarkerKey = "repopulse.auth.session-invalidated.v1";

    public Task<bool> IsSetAsync() => Task.FromResult(Preferences.Default.Get(MarkerKey, false));

    public Task SetAsync()
    {
        Preferences.Default.Set(MarkerKey, true);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Preferences.Default.Remove(MarkerKey);
        return Task.CompletedTask;
    }
}
