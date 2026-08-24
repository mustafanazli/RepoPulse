using RepoPulse.Core.Navigation;

namespace RepoPulse.UnitTests;

public class NavigationGuardTests
{
    [Theory]
    [InlineData(AppRoutes.RepositoryList)]
    [InlineData(AppRoutes.RepositoryDetail)]
    [InlineData(AppRoutes.Settings)]
    public void ProtectedRoute_WhenNotSignedIn_IsRejected(string route)
    {
        Assert.False(NavigationGuard.IsNavigationAllowed(route, isSignedIn: false));
    }

    [Theory]
    [InlineData(AppRoutes.RepositoryList)]
    [InlineData(AppRoutes.RepositoryDetail)]
    [InlineData(AppRoutes.Settings)]
    public void ProtectedRoute_WhenSignedIn_IsAllowed(string route)
    {
        Assert.True(NavigationGuard.IsNavigationAllowed(route, isSignedIn: true));
    }

    [Theory]
    [InlineData("repositories")]
    [InlineData("//repositories")]
    [InlineData("//repositoryDetail")]
    [InlineData("//settings")]
    public void AbsoluteOrRelativeProtectedRouteLocation_WhenNotSignedIn_IsRejected(string location)
    {
        Assert.False(NavigationGuard.IsNavigationAllowed(location, isSignedIn: false));
    }

    [Fact]
    public void LoginRoute_IsAlwaysAllowed_RegardlessOfSignInState()
    {
        Assert.True(NavigationGuard.IsNavigationAllowed($"//{AppRoutes.Login}", isSignedIn: false));
        Assert.True(NavigationGuard.IsNavigationAllowed($"//{AppRoutes.Login}", isSignedIn: true));
    }

    [Fact]
    public void UnknownRoute_IsNotTreatedAsProtected()
    {
        Assert.True(NavigationGuard.IsNavigationAllowed("someUnrelatedRoute", isSignedIn: false));
    }
}
