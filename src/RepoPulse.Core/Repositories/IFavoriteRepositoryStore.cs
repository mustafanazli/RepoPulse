namespace RepoPulse.Core.Repositories;

// RP-012: MAUI/SQLite-independent contract for persisted repository
// favorites. The real implementation (SqliteFavoriteRepositoryStore) lives
// in RepoPulse.Infrastructure; this interface is all RepoPulse.Core and the
// MAUI app ever depend on, so FavoriteToggleController and the page
// code-behind stay testable without a real database.
public interface IFavoriteRepositoryStore
{
    // Idempotent and safe to call more than once (including concurrently,
    // including from multiple store instances pointing at the same file) —
    // every other method calls this internally too, so an explicit call is
    // only useful for surfacing a schema/IO failure before the first real
    // operation.
    Task<FavoriteStoreResult> InitializeAsync(CancellationToken cancellationToken);

    Task<FavoriteListResult> GetAllAsync(CancellationToken cancellationToken);

    // Idempotent: adding an (owner, name) that is already a favorite (by
    // FavoriteRepositoryIdentifier's case-insensitive identity) is a no-op
    // that leaves the existing row — including its original AddedAtUtc —
    // untouched, rather than creating a duplicate or bumping the timestamp.
    Task<FavoriteStoreResult> AddAsync(string owner, string name, DateTimeOffset addedAtUtc, CancellationToken cancellationToken);

    Task<FavoriteStoreResult> RemoveAsync(string owner, string name, CancellationToken cancellationToken);

    Task<FavoriteStatusResult> IsFavoriteAsync(string owner, string name, CancellationToken cancellationToken);
}
