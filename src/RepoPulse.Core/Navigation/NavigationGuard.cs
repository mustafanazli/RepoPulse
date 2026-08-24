namespace RepoPulse.Core.Navigation;

// Pure decision logic behind AppShell's OnNavigating guard (RP-007) —
// extracted so "a protected route is rejected while signed out" is
// unit-testable without a running Shell/MAUI host. AppShell.xaml.cs calls
// this directly and must never duplicate the protected-route list itself.
public static class NavigationGuard
{
    private static readonly string[] ProtectedRoutes =
    {
        AppRoutes.RepositoryList,
        AppRoutes.RepositoryDetail,
        AppRoutes.Settings
    };

    public static bool IsProtectedRoute(string targetLocation) =>
        !string.IsNullOrEmpty(targetLocation) &&
        ProtectedRoutes.Any(route => targetLocation.Contains(route, StringComparison.OrdinalIgnoreCase));

    public static bool IsNavigationAllowed(string targetLocation, bool isSignedIn) =>
        isSignedIn || !IsProtectedRoute(targetLocation);
}
