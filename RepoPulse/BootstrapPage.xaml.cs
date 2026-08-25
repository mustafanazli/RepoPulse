using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;

namespace RepoPulse;

// Shown for the brief window while the persisted session (if any) is read
// from SecureStorage and validated (RP-008). Replaces AppShell's initial
// CurrentItem so LoginPage never flashes on a cold start that turns out to
// already be signed in. Never a protected route (see NavigationGuard); both
// destinations below are absolute routes, so nothing ever navigates back
// here after the very first frame.
public partial class BootstrapPage : ContentPage
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(10);

    private readonly SessionPersistenceStore sessionPersistenceStore;
    private bool hasStarted;

    public BootstrapPage(SessionPersistenceStore sessionPersistenceStore)
    {
        InitializeComponent();
        this.sessionPersistenceStore = sessionPersistenceStore;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (hasStarted)
        {
            return;
        }

        hasStarted = true;

        bool restored;
        try
        {
            using var cts = new CancellationTokenSource(RestoreTimeout);
            restored = await sessionPersistenceStore.RestoreAsync(DateTimeOffset.UtcNow, cts.Token);
        }
        catch (Exception)
        {
            // Restore never makes a network call and every internal failure
            // path already resolves to "not restored" — this catch only
            // guards against something unexpected (e.g. cancellation)
            // reaching here uncaught. Falling back to Login is always safe.
            restored = false;
        }

        var destination = restored ? AppRoutes.RepositoryList : AppRoutes.Login;

        try
        {
            await Shell.Current.GoToAsync($"//{destination}");
        }
        catch (Exception)
        {
            // Nothing user-actionable to surface beyond staying on this
            // loading screen — there is no retry affordance on it by design.
        }
    }
}
