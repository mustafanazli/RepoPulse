using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;

namespace RepoPulse;

// Only the single, real capability RP-007 delivers: showing who is signed
// in and letting them sign out. No placeholder/not-yet-implemented options.
public partial class SettingsPage : ContentPage
{
    private static readonly TimeSpan SignOutTimeout = TimeSpan.FromSeconds(10);

    private readonly UserSessionStore userSessionStore;
    private readonly SessionPersistenceStore sessionPersistenceStore;
    private bool isSigningOut;

    public SettingsPage(UserSessionStore userSessionStore, SessionPersistenceStore sessionPersistenceStore)
    {
        InitializeComponent();
        this.userSessionStore = userSessionStore;
        this.sessionPersistenceStore = sessionPersistenceStore;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        SignOutStatusLabel.IsVisible = false;

        var session = userSessionStore.Current;
        GitHubLoginLabel.Text = session is not null ? $"@{session.Login}" : string.Empty;

        if (!string.IsNullOrEmpty(session?.AvatarUrl))
        {
            GitHubAvatarImage.Source = session.AvatarUrl;
        }
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        if (isSigningOut)
        {
            return;
        }

        isSigningOut = true;
        SignOutButton.IsEnabled = false;

        try
        {
            using var cts = new CancellationTokenSource(SignOutTimeout);

            // SessionPersistenceStore removes the persisted key and only
            // then clears UserSessionStore — a false here means the
            // persisted session may still be on disk, so the old session
            // must not be allowed to look "gone"; stay put and let the
            // user retry rather than navigating away.
            var signedOut = await sessionPersistenceStore.SignOutAsync(cts.Token);
            if (!signedOut)
            {
                SetStatus("Çıkış yapılamadı, lütfen tekrar deneyin.");
                SignOutButton.IsEnabled = true;
                return;
            }

            try
            {
                // Absolute route ("//") replaces the whole navigation
                // stack — the back button/gesture can never return to a
                // protected page after this.
                await Shell.Current.GoToAsync($"//{AppRoutes.Login}");
            }
            catch (Exception)
            {
                SignOutButton.IsEnabled = true;
            }
        }
        catch (Exception)
        {
            SetStatus("Çıkış yapılamadı, lütfen tekrar deneyin.");
            SignOutButton.IsEnabled = true;
        }
        finally
        {
            isSigningOut = false;
        }
    }

    private void SetStatus(string statusText)
    {
        SignOutStatusLabel.Text = statusText;
        SignOutStatusLabel.IsVisible = true;
    }
}
