using RepoPulse.Core.Authentication;

namespace RepoPulse.Core.Repositories;

// RP-012 (fixed): MAUI-independent presentation-state controller for the
// favorite toggle, mirroring how RepositoryListController (RP-010) owns
// non-visual list-loading state. Registered as a single DI instance shared
// by RepositoryListPage and RepositoryDetailPage (see MauiProgram) so
// toggling a favorite on either page is immediately visible on the other
// without any extra sync step — both pages are reading the exact same
// in-memory state.
//
// Favorites ARE scoped to the currently signed-in GitHub account
// (UserSessionStore.Current.Login) — the very first version of this
// controller/store deliberately was not, which leaked one account's
// favorites into another account's view on the same device. Every sign-in
// AND sign-out bumps UserSessionStore.SessionGeneration, so
// EnsureLoadedForCurrentSessionAsync reloads (and, on sign-out, simply
// clears) in-memory state on every session change — including signing back
// into the very same account, which correctly restores that account's own
// favorites rather than leaving them cleared.
public sealed class FavoriteToggleController
{
    private readonly IFavoriteRepositoryStore store;
    private readonly TimeProvider timeProvider;
    private readonly UserSessionStore userSessionStore;
    private readonly Dictionary<string, FavoriteRepository> favoritesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingKeys = new(StringComparer.Ordinal);
    private long? loadedForSessionGeneration;

    public FavoriteToggleController(IFavoriteRepositoryStore store, TimeProvider timeProvider, UserSessionStore userSessionStore)
    {
        this.store = store;
        this.timeProvider = timeProvider;
        this.userSessionStore = userSessionStore;
    }

    // Non-null only after a reload whose GetAllAsync failed — lets a caller
    // distinguish "loaded, zero favorites" from "failed to load, showing
    // stale/no data" without needing its own result plumbing.
    public FavoriteStoreFailureKind? LastLoadFailure { get; private set; }

    // Always scoped to whichever account EnsureLoadedForCurrentSessionAsync
    // last loaded for — see that method's doc comment for the invariant
    // callers must uphold (call it before relying on this being correct for
    // the CURRENT session).
    public IReadOnlyCollection<FavoriteRepository> Favorites => favoritesByKey.Values;

    // A no-op when already loaded for the current SessionGeneration (RP-010's
    // exact HasLoadedFor pattern) — cheap to call on every OnAppearing.
    // Signing out (Current is null) clears in-memory state without a store
    // call, since there is no account to load for. Callers MUST await this
    // before trusting IsFavorite/Favorites/ToggleAsync to reflect the
    // correct account — RepositoryListPage does so in OnAppearing before any
    // favorite-toggle UI can be interacted with.
    public async Task EnsureLoadedForCurrentSessionAsync(CancellationToken cancellationToken)
    {
        var currentGeneration = userSessionStore.SessionGeneration;
        if (loadedForSessionGeneration == currentGeneration)
        {
            return;
        }

        // A previous account's favorites must never remain visible for even
        // one render after sign-out/switch — cleared unconditionally before
        // the (possibly failing, possibly slow) reload below.
        favoritesByKey.Clear();
        LastLoadFailure = null;

        var session = userSessionStore.Current;
        if (session is null)
        {
            loadedForSessionGeneration = currentGeneration;
            return;
        }

        var result = await store.GetAllAsync(session.Login, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            foreach (var favorite in result.Favorites)
            {
                favoritesByKey[favorite.NormalizedFullName] = favorite;
            }
        }
        else
        {
            LastLoadFailure = result.FailureKind;
        }

        loadedForSessionGeneration = currentGeneration;
    }

    public bool IsFavorite(string owner, string name) =>
        FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity) &&
        favoritesByKey.ContainsKey(identity.NormalizedFullName);

    // A second ToggleAsync for the same (owner, name) while the first is
    // still awaiting the store never issues a second AddAsync/RemoveAsync —
    // it returns Ignored() immediately instead. Different identities toggle
    // fully independently and concurrently. Always resolves the account from
    // UserSessionStore.Current at call time (never cached), so it is always
    // scoped to whoever is signed in right now.
    public async Task<FavoriteToggleResult> ToggleAsync(string owner, string name, CancellationToken cancellationToken)
    {
        var session = userSessionStore.Current;
        if (session is null)
        {
            return FavoriteToggleResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }

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
                ? await store.RemoveAsync(session.Login, identity.Owner, identity.Name, cancellationToken).ConfigureAwait(false)
                : await store.AddAsync(session.Login, identity.Owner, identity.Name, addedAtUtc, cancellationToken).ConfigureAwait(false);

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
