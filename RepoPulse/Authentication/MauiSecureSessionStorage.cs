using RepoPulse.Core.Authentication;

namespace RepoPulse.Authentication;

// Faithful pass-through to MAUI's SecureStorage.Default under one fixed,
// non-sensitive key (RP-008). Deliberately does NOT catch anything here —
// SessionPersistenceStore owns every failure path (including the Android
// backup/restore undecryptable-value scenario) so that behavior is covered
// by plain unit tests against a fake ISecureSessionStorage. Touches exactly
// this one key — never SecureStorage.Default.RemoveAll().
public sealed class MauiSecureSessionStorage : ISecureSessionStorage
{
    public const string StorageKey = "repopulse.auth.session.v1";

    public async Task<string?> GetAsync() => await SecureStorage.Default.GetAsync(StorageKey);

    public Task SetAsync(string value) => SecureStorage.Default.SetAsync(StorageKey, value);

    public Task RemoveAsync()
    {
        SecureStorage.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }
}
