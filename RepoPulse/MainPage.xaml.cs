using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;

namespace RepoPulse
{
    public partial class MainPage : ContentPage
    {
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private readonly IRepoPulseAuthApiClient authApiClient;
        private readonly IGitHubApiClient gitHubApiClient;
        private readonly AuthorizationSessionStore sessionStore;

        int count = 0;
        bool isSubscribedToOAuthCallbacks;
        bool isSignInInProgress;

        // In-memory only, by design (RP-005) — never SecureStorage/SQLite/
        // Preferences/file. Lost on app restart; that is acceptable for now.
        // Not used for anything yet (no refresh flow implemented).
        private string? currentAccessToken;
        private string? currentRefreshToken;

        public MainPage(IRepoPulseAuthApiClient authApiClient, IGitHubApiClient gitHubApiClient, AuthorizationSessionStore sessionStore)
        {
            InitializeComponent();
            this.authApiClient = authApiClient;
            this.gitHubApiClient = gitHubApiClient;
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

            var exchangeResult = await authApiClient.ExchangeAsync(result.Code!, session.CodeVerifier, cts.Token);
            if (!exchangeResult.IsSuccess || exchangeResult.Success is null)
            {
                SetStatus(DescribeExchangeFailure(exchangeResult.FailureKind));
                EndSignInAttempt();
                return;
            }

            currentAccessToken = exchangeResult.Success.AccessToken;
            currentRefreshToken = exchangeResult.Success.RefreshToken;

            var userResult = await gitHubApiClient.GetCurrentUserAsync(currentAccessToken, cts.Token);
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

        // Maps the backend's error contract (docs/backend-auth.md) to a short,
        // safe Turkish message — never the raw backend response body or an
        // exception message.
        private static string DescribeExchangeFailure(AuthApiExchangeFailureKind kind) => kind switch
        {
            AuthApiExchangeFailureKind.InvalidRequest => "Giriş isteği geçersiz.",
            AuthApiExchangeFailureKind.OAuthExchangeFailed => "GitHub yetkilendirmeyi reddetti.",
            AuthApiExchangeFailureKind.UpstreamError => "GitHub şu anda yanıt vermiyor.",
            AuthApiExchangeFailureKind.UpstreamTimeout => "GitHub'a bağlanırken zaman aşımı oluştu.",
            AuthApiExchangeFailureKind.RateLimited => "Çok fazla deneme yapıldı, biraz sonra tekrar deneyin.",
            AuthApiExchangeFailureKind.InternalError => "Sunucuda beklenmeyen bir hata oluştu.",
            AuthApiExchangeFailureKind.NetworkError => "Sunucuya ulaşılamadı.",
            AuthApiExchangeFailureKind.Timeout => "İstek zaman aşımına uğradı.",
            AuthApiExchangeFailureKind.MalformedResponse => "Sunucudan geçersiz bir yanıt alındı.",
            _ => "Giriş başarısız oldu."
        };

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
