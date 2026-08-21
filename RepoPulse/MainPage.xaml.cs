using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;

namespace RepoPulse
{
    public partial class MainPage : ContentPage
    {
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private readonly GitHubOAuthClient oauthClient;
        private readonly AuthorizationSessionStore sessionStore;

        int count = 0;
        bool isSubscribedToOAuthCallbacks;
        bool isSignInInProgress;

        public MainPage(GitHubOAuthClient oauthClient, AuthorizationSessionStore sessionStore)
        {
            InitializeComponent();
            this.oauthClient = oauthClient;
            this.sessionStore = sessionStore;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (!isSubscribedToOAuthCallbacks)
            {
                OAuthCallbackBroker.CallbackReceived += OnOAuthCallbackReceived;
                isSubscribedToOAuthCallbacks = true;
            }

            var pending = OAuthCallbackBroker.TryConsumePendingResult();
            if (pending is not null)
            {
                OnOAuthCallbackReceived(this, pending);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            if (isSubscribedToOAuthCallbacks)
            {
                OAuthCallbackBroker.CallbackReceived -= OnOAuthCallbackReceived;
                isSubscribedToOAuthCallbacks = false;
            }
        }

        private void OnCounterClicked(object? sender, EventArgs e)
        {
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";

            SemanticScreenReader.Announce(CounterBtn.Text);
        }

        private async void OnSignInClicked(object? sender, EventArgs e)
        {
            if (isSignInInProgress)
            {
                return;
            }

            if (!sessionStore.TryStart(SessionLifetime, out var session))
            {
                // A concurrent sign-in attempt is already pending; reject this
                // one instead of overwriting it.
                SetStatus("Zaten devam eden bir giriş denemesi var.");
                return;
            }

            isSignInInProgress = true;
            SignInButton.IsEnabled = false;
            SetSignedOutUi();
            SetStatus("GitHub'a yönlendiriliyor...");

            var authorizationUrl = GitHubAuthorizationUrlBuilder.Build(
                OAuthConstants.GitHubClientId,
                OAuthConstants.RedirectUri,
                session.State,
                session.CodeChallenge);

            try
            {
                await Browser.Default.OpenAsync(new Uri(authorizationUrl), BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception)
            {
                sessionStore.Reset();
                SetStatus("Tarayıcı açılamadı.");
                EndSignInAttempt();
            }
        }

        // Dev-only status text for RP-002/RP-003 callback verification. Deliberately
        // never displays the actual code/state/error_description/token values.
        private async void OnOAuthCallbackReceived(object? sender, OAuthCallbackResult result)
        {
            switch (result.Outcome)
            {
                case OAuthCallbackOutcome.Success:
                    await HandleSuccessfulCallbackAsync(result);
                    break;

                case OAuthCallbackOutcome.Cancelled:
                    sessionStore.Reset();
                    SetStatus("Kullanıcı iptal etti");
                    EndSignInAttempt();
                    break;

                case OAuthCallbackOutcome.Invalid:
                default:
                    sessionStore.Reset();
                    SetStatus("Geçersiz callback");
                    EndSignInAttempt();
                    break;
            }
        }

        private async Task HandleSuccessfulCallbackAsync(OAuthCallbackResult result)
        {
            if (!sessionStore.TryConsume(result.State, out var session) || session is null)
            {
                // Wrong/missing/expired/already-used state: never send the token
                // request for a callback we cannot attribute to our own session.
                SetStatus("Geçersiz callback");
                EndSignInAttempt();
                return;
            }

            SetStatus("Doğrulanıyor...");

            using var cts = new CancellationTokenSource(RequestTimeout);

            var tokenResult = await oauthClient.ExchangeCodeForTokenAsync(result.Code!, session.CodeVerifier, cts.Token);
            if (!tokenResult.IsSuccess || tokenResult.Success is null)
            {
                SetStatus($"Giriş başarısız: {tokenResult.SafeErrorMessage}");
                EndSignInAttempt();
                return;
            }

            var userResult = await oauthClient.GetCurrentUserAsync(tokenResult.Success.AccessToken, cts.Token);
            if (!userResult.IsSuccess || userResult.User is null)
            {
                SetStatus($"Giriş başarısız: {userResult.SafeErrorMessage}");
                EndSignInAttempt();
                return;
            }

            SetStatus("Callback alındı");
            SetSignedInUi(userResult.User);
            EndSignInAttempt();
        }

        private void EndSignInAttempt()
        {
            isSignInInProgress = false;
            MainThread.BeginInvokeOnMainThread(() => SignInButton.IsEnabled = true);
        }

        private void SetStatus(string statusText) =>
            MainThread.BeginInvokeOnMainThread(() => OAuthCallbackStatusLabel.Text = statusText);

        private void SetSignedInUi(GitHubUser user)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                GitHubLoginLabel.Text = $"Giriş yapan: @{user.Login}";
                GitHubLoginLabel.IsVisible = true;

                if (!string.IsNullOrEmpty(user.AvatarUrl))
                {
                    GitHubAvatarImage.Source = user.AvatarUrl;
                    GitHubAvatarImage.IsVisible = true;
                }
            });
        }

        private void SetSignedOutUi()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                GitHubLoginLabel.IsVisible = false;
                GitHubAvatarImage.IsVisible = false;
            });
        }
    }
}
