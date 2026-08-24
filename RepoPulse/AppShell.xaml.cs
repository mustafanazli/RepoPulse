using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;

namespace RepoPulse;

// Centralizes navigation registration and the sign-in guard (RP-007).
// FlyoutBehavior is disabled in AppShell.xaml, so this OnNavigating override
// is the ONLY gate between an unauthenticated user and RepositoryList /
// RepositoryDetail / Settings — there is no flyout/tab UI to route around it.
public partial class AppShell : Shell
{
    private readonly UserSessionStore userSessionStore;

    public AppShell(UserSessionStore userSessionStore)
    {
        InitializeComponent();
        this.userSessionStore = userSessionStore;

        // Works around a MAUI Shell issue where two bare ShellContent
        // siblings (no TabBar/FlyoutItem wrapper) can leave CurrentItem
        // unset, which crashes the Android ShellItemRenderer with
        // "Active Shell Item not set" on first launch.
        CurrentItem = LoginContent;

        // RepositoryList and Login are declared as ShellContent in
        // AppShell.xaml (so they're reachable with an absolute "//" route
        // that resets the whole back stack). Detail and Settings are pushed
        // relatively on top of RepositoryList, so they're registered here
        // instead of being ShellContent themselves.
        Routing.RegisterRoute(AppRoutes.RepositoryDetail, typeof(RepositoryDetailPage));
        Routing.RegisterRoute(AppRoutes.Settings, typeof(SettingsPage));
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);

        // Shell fires this event for its own internal bootstrap navigation
        // (selecting the initial CurrentItem) before the shell is fully
        // attached — args/its members can be in a degenerate state at that
        // point. The guard below must never crash app startup, so any
        // failure reading navigation state is treated as "allow" rather
        // than propagating.
        try
        {
            if (args.Cancelled)
            {
                return;
            }

            var target = args.Target?.Location?.OriginalString ?? string.Empty;

            if (!NavigationGuard.IsNavigationAllowed(target, userSessionStore.IsSignedIn))
            {
                args.Cancel();
                RedirectToLogin();
            }
        }
        catch (Exception)
        {
        }
    }

    private void RedirectToLogin()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Shell.Current.GoToAsync($"//{AppRoutes.Login}");
            }
            catch (Exception)
            {
                // Nothing user-actionable to surface here — worst case the
                // cancelled navigation simply leaves the current page shown.
            }
        });
    }
}
