namespace RepoPulse.Core.Repositories;

// Pure, MAUI-independent client-side search + sort over an already-loaded
// repository list (RP-011). Never issues a GitHub request — RP-009's
// GetUserRepositoriesAsync/pagination is completely untouched — and never
// mutates the source list or RepositoryListController's State/session-
// generation cache key (the access-token-retention fix from the previous
// turn). RepositoryListPage re-applies this after every render, keeping the
// search text and sort order as its own page-local, ephemeral UI state
// rather than pushing them into the controller.
public static class RepositoryListProjection
{
    public static IReadOnlyList<GitHubRepository> Apply(
        IReadOnlyList<GitHubRepository> repositories,
        string? searchText,
        RepositorySortOrder sortOrder)
    {
        var query = searchText?.Trim();

        IEnumerable<GitHubRepository> filtered = string.IsNullOrEmpty(query)
            ? repositories
            : repositories.Where(repository => Matches(repository, query));

        return Sort(filtered, sortOrder).ToList();
    }

    private static bool Matches(GitHubRepository repository, string query) =>
        repository.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (repository.Description is not null && repository.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<GitHubRepository> Sort(IEnumerable<GitHubRepository> repositories, RepositorySortOrder sortOrder) =>
        sortOrder switch
        {
            RepositorySortOrder.NameAscending => repositories
                .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(repository => repository.FullName, StringComparer.Ordinal),
            // Repositories with an UpdatedAt value always sort before those
            // without one, regardless of how the comparer treats nulls —
            // HasValue is compared explicitly first so this never depends on
            // that implementation detail. Among repositories that share the
            // same instant (including the whole no-UpdatedAt group, which
            // all map to the same tie-break bucket), FullName breaks the tie
            // so the result is fully deterministic.
            _ => repositories
                .OrderByDescending(repository => repository.UpdatedAt.HasValue)
                .ThenByDescending(repository => repository.UpdatedAt ?? DateTimeOffset.MinValue)
                .ThenBy(repository => repository.FullName, StringComparer.Ordinal)
        };
}
