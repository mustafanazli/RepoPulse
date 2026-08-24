using RepoPulse.Core.Repositories;

namespace RepoPulse.Core.Navigation;

// Builds the Shell query dictionary RepositoryListPage passes to
// RepositoryDetailPage — extracted to its own MAUI-independent type so
// "a repository selection never carries the access token" is unit-testable
// (RP-007). The dictionary carries exactly one entry: the already-fetched
// GitHubRepository object itself, which has no token-shaped field.
public static class RepositoryNavigationQueryBuilder
{
    public static IReadOnlyDictionary<string, object> Build(GitHubRepository repository) =>
        new Dictionary<string, object>
        {
            [AppRoutes.RepositoryQueryKey] = repository
        };
}
