namespace RepoPulse.Core.Authentication;

// Thin abstraction over the platform secure storage mechanism (backed by
// MAUI's SecureStorage.Default in the app project) so SessionPersistenceStore
// stays MAUI-independent and unit-testable with a fake (RP-008). The
// implementation is expected to touch exactly one fixed, non-sensitive key —
// it must never wipe unrelated secure values (i.e. never call the
// platform's "remove everything" API).
public interface ISecureSessionStorage
{
    Task<string?> GetAsync();

    Task SetAsync(string value);

    Task RemoveAsync();
}
