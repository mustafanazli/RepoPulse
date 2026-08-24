using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;

namespace RepoPulse;

// Only the single, real capability RP-007 delivers: showing who is signed
// in and letting them sign out. No placeholder/not-yet-implemented options.
public partial class SettingsPage : ContentPage
{
    private readonly UserSessionStore userSessionStore;
    private bool isSigningOut;

    public SettingsPage(UserSessionStore userSessionStore)
    {
        InitializeComponent();
        this.userSessionStore = userSessionStore;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

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

        userSessionStore.SignOut();

        try
        {
            // Absolute route ("//") replaces the whole navigation stack —
            // the back button/gesture can never return to a protected page
            // after this.
            await Shell.Current.GoToAsync($"//{AppRoutes.Login}");
        }
        catch (Exception)
        {
            SignOutButton.IsEnabled = true;
        }
        finally
        {
            isSigningOut = false;
        }
    }
}
