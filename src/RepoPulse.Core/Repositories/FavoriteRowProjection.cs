namespace RepoPulse.Core.Repositories;

// RP-012: pure, MAUI-independent projection behind RepositoryListPage's
// "Favoriler" view — mirrors how RepositoryListProjection (RP-011) backs
// "Tümü". Never issues a GitHub request and never mutates its inputs.
// Returns a mix of RepositoryListItem (the favorite is present in
// latestRepositories — shown as a full card) and FavoriteIdentityRow (it
// isn't — shown as an identity-only row with no fabricated stars/
// description/language). Deterministic order: most-recently-favorited
// first, tied instants broken by the normalized identity.
public static class FavoriteRowProjection
{
    public static IReadOnlyList<object> Apply(
        IReadOnlyList<GitHubRepository> latestRepositories,
        IReadOnlyCollection<FavoriteRepository> favorites,
        string? searchText)
    {
        var query = searchText?.Trim();
        var liveByNormalizedName = latestRepositories.ToDictionary(
            repository => FavoriteRepositoryIdentifier.NormalizeFullName(repository.FullName),
            repository => repository,
            StringComparer.Ordinal);

        IEnumerable<FavoriteRepository> filtered = favorites;
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(favorite => favorite.NormalizedFullName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var rows = new List<object>();
        foreach (var favorite in filtered
            .OrderByDescending(favorite => favorite.AddedAtUtc)
            .ThenBy(favorite => favorite.NormalizedFullName, StringComparer.Ordinal))
        {
            rows.Add(liveByNormalizedName.TryGetValue(favorite.NormalizedFullName, out var liveRepository)
                ? RepositoryListItem.FromRepository(liveRepository, isFavorite: true)
                : FavoriteIdentityRow.FromFavorite(favorite));
        }

        return rows;
    }
}
