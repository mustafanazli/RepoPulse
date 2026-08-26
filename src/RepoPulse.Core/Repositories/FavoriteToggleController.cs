namespace RepoPulse.Core.Repositories;

// RP-012: MAUI-independent presentation-state controller for the favorite
// toggle, mirroring how RepositoryListController (RP-010) owns non-visual
// list-loading state. Registered as a single DI instance shared by
// RepositoryListPage and RepositoryDetailPage (see MauiProgram) so toggling
// a favorite on either page is immediately visible on the other without any
// extra sync step — both pages are reading the exact same in-memory state.
//
// Favorites are intentionally NOT scoped to the current GitHub session/
// account (RP-012 explicitly excludes multi-account favorites) — this
// controller never reads UserSessionStore and never needs to reload when
// the session generation changes, unlike RepositoryListController.
public sealed class FavoriteToggleController
{
    private readonly IFavoriteRepositoryStore store;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<string, FavoriteRepository> favoritesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingKeys = new(StringComparer.Ordinal);

    public FavoriteToggleController(IFavoriteRepositoryStore store, TimeProvider timeProvider)
    {
        this.store = store;
        this.timeProvider = timeProvider;
    }

    // Non-null only after a LoadAsync call whose GetAllAsync failed — lets a
    // caller distinguish "loaded, zero favorites" from "failed to load,
    // showing yesterday's/no data" without needing its own result plumbing.
    public FavoriteStoreFailureKind? LastLoadFailure { get; private set; }

    public IReadOnlyCollection<FavoriteRepository> Favorites => favoritesByKey.Values;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var result = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            LastLoadFailure = result.FailureKind;
            return;
        }

        LastLoadFailure = null;
        favoritesByKey.Clear();
        foreach (var favorite in result.Favorites)
        {
            favoritesByKey[favorite.NormalizedFullName] = favorite;
        }
    }

    public bool IsFavorite(string owner, string name) =>
        FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity) &&
        favoritesByKey.ContainsKey(identity.NormalizedFullName);

    // A second ToggleAsync for the same (owner, name) while the first is
    // still awaiting the store never issues a second AddAsync/RemoveAsync —
    // it returns Ignored() immediately instead. Different identities toggle
    // fully independently and concurrently.
    public async Task<FavoriteToggleResult> ToggleAsync(string owner, string name, CancellationToken cancellationToken)
    {
        if (!FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity))
        {
            return FavoriteToggleResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }

        if (!pendingKeys.Add(identity.NormalizedFullName))
        {
            return FavoriteToggleResult.Ignored();
        }

        try
        {
            var wasFavorite = favoritesByKey.ContainsKey(identity.NormalizedFullName);
            var addedAtUtc = timeProvider.GetUtcNow();

            var result = wasFavorite
                ? await store.RemoveAsync(identity.Owner, identity.Name, cancellationToken).ConfigureAwait(false)
                : await store.AddAsync(identity.Owner, identity.Name, addedAtUtc, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return FavoriteToggleResult.Failure(result.FailureKind!.Value);
            }

            if (wasFavorite)
            {
                favoritesByKey.Remove(identity.NormalizedFullName);
            }
            else
            {
                favoritesByKey[identity.NormalizedFullName] = new FavoriteRepository(identity.Owner, identity.Name, identity.NormalizedFullName, addedAtUtc);
            }

            return FavoriteToggleResult.Success(!wasFavorite);
        }
        finally
        {
            pendingKeys.Remove(identity.NormalizedFullName);
        }
    }
}
