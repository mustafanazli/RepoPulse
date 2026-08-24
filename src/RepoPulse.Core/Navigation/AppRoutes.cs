namespace RepoPulse.Core.Navigation;

// Single source of truth for every Shell route name and the one query key
// used to pass a repository between pages (RP-007) — so a route/query-key
// typo can't silently create two different strings that mean the same
// thing. MAUI-independent (plain strings) so it's unit-testable without a
// Shell/page dependency; the RepoPulse app project's AppShell/pages use
// these constants directly.
public static class AppRoutes
{
    public const string Login = "login";
    public const string RepositoryList = "repositories";
    public const string RepositoryDetail = "repositoryDetail";
    public const string Settings = "settings";

    // Key under which RepositoryListPage passes the already-fetched
    // GitHubRepository (never the access token) to RepositoryDetailPage.
    public const string RepositoryQueryKey = "Repository";
}
