namespace RepoPulse.Core.Repositories;

// RP-012: MAUI/SQLite-independent contract for persisted repository
// favorites. The real implementation (SqliteFavoriteRepositoryStore) lives
// in RepoPulse.Infrastructure; this interface is all RepoPulse.Core and the
// MAUI app ever depend on, so FavoriteToggleController and the page
// code-behind stay testable without a real database.
//
// Every data operation is scoped by accountLogin (the signed-in GitHub
// login — UserSession.Login, never a token/hash) — fixing a cross-account
// leak where the very first version of this store kept a single global
// favorites table shared by every account that ever signed in on the
// device. accountLogin is normalized internally (Trim + ToLowerInvariant,
// same technique as repository identity) exactly like
// FavoriteRepositoryIdentifier.TryNormalizeAccountLogin.
public interface IFavoriteRepositoryStore
{
    // Idempotent and safe to call more than once (including concurrently,
    // including from multiple store instances pointing at the same file) —
    // every other method calls this internally too, so an explicit call is
    // only useful for surfacing a schema/IO failure before the first real
    // operation.
    Task<FavoriteStoreResult> InitializeAsync(CancellationToken cancellationToken);

    Task<FavoriteListResult> GetAllAsync(string accountLogin, CancellationToken cancellationToken);

    // Idempotent: adding an (owner, name) that is already a favorite for
    // this same account (by FavoriteRepositoryIdentifier's case-insensitive
    // identity) is a no-op that leaves the existing row — including its
    // original AddedAtUtc — untouched, rather than creating a duplicate or
    // bumping the timestamp. A different account favoriting the exact same
    // repository is a fully independent row.
    Task<FavoriteStoreResult> AddAsync(string accountLogin, string owner, string name, DateTimeOffset addedAtUtc, CancellationToken cancellationToken);

    Task<FavoriteStoreResult> RemoveAsync(string accountLogin, string owner, string name, CancellationToken cancellationToken);

    Task<FavoriteStatusResult> IsFavoriteAsync(string accountLogin, string owner, string name, CancellationToken cancellationToken);
}
