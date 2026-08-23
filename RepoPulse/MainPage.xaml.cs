using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;

namespace RepoPulse
{
    public partial class MainPage : ContentPage
    {
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private readonly IRepoPulseAuthApiClient authApiClient;
        private readonly IGitHubApiClient gitHubApiClient;
        private readonly AuthorizationSessionStore sessionStore;

        bool isSubscribedToOAuthCallbacks;
        bool isSignInInProgress;
        bool isRepositoryLookupInProgress;

        // In-memory only, by design (RP-005) — never SecureStorage/SQLite/
        // Preferences/file. Lost on app restart; that is acceptable for now.
        // currentAccessToken is also used for the repository lookup call
        // (RP-006) — still never persisted anywhere beyond this field.
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

        // Deliberately never displays the actual code/state/error_description/
        // token values — only short, safe, user-facing status text.
        private async void OnOAuthCallbackReceived(object? sender, OAuthCallbackResult result)
        {
            switch (result.Outcome)
            {
                case OAuthCallbackOutcome.Success:
                    await HandleSuccessfulCallbackAsync(result);
                    break;

                case OAuthCallbackOutcome.Cancelled:
                    sessionStore.Reset();
                    SetStatus("Giriş iptal edildi.");
                    EndSignInAttempt();
                    break;

                case OAuthCallbackOutcome.Invalid:
                default:
                    sessionStore.Reset();
                    SetStatus("Giriş isteği doğrulanamadı, lütfen tekrar deneyin.");
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
                SetStatus("Giriş isteği doğrulanamadı, lütfen tekrar deneyin.");
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OAuthCallbackStatusLabel.Text = statusText;
                OAuthCallbackStatusLabel.IsVisible = true;
            });

        private void SetSignedInUi(GitHubUser user)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OAuthCallbackStatusLabel.IsVisible = false;
                SignInButton.IsVisible = false;

                GitHubLoginLabel.Text = $"@{user.Login}";
                SignedInHeader.IsVisible = true;

                if (!string.IsNullOrEmpty(user.AvatarUrl))
                {
                    GitHubAvatarImage.Source = user.AvatarUrl;
                }

                RepositoryLookupSection.IsVisible = true;
            });
        }

        private void SetSignedOutUi()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SignInButton.IsVisible = true;
                SignedInHeader.IsVisible = false;
                RepositoryLookupSection.IsVisible = false;
                ClearRepositoryResult();
            });
        }

        // --- Repository lookup (RP-006) ---

        private async void OnLookupRepositoryClicked(object? sender, EventArgs e)
        {
            if (isRepositoryLookupInProgress)
            {
                // A lookup is already in flight — a double-tap must never
                // start a second concurrent request.
                return;
            }

            var parseResult = RepositoryIdentifierParser.Parse(RepositoryInput.Text);
            if (!parseResult.IsSuccess || parseResult.Value is null)
            {
                SetRepositoryError(parseResult.SafeErrorMessage ?? "Repository adı geçersiz.");
                return;
            }

            if (currentAccessToken is null)
            {
                SetRepositoryError("Önce GitHub ile giriş yapmalısınız.");
                return;
            }

            isRepositoryLookupInProgress = true;
            LookupButton.IsEnabled = false;
            SetRepositoryLoading();

            using var cts = new CancellationTokenSource(RequestTimeout);
            var repositoryResult = await gitHubApiClient.GetRepositoryAsync(
                currentAccessToken,
                parseResult.Value.Owner,
                parseResult.Value.Name,
                cts.Token);

            isRepositoryLookupInProgress = false;
            LookupButton.IsEnabled = true;

            if (!repositoryResult.IsSuccess || repositoryResult.Repository is null)
            {
                SetRepositoryError(DescribeRepositoryFailure(repositoryResult.FailureKind));
                return;
            }

            SetRepositoryResult(repositoryResult.Repository);
        }

        // Maps the typed failure kind to a short, safe Turkish message —
        // never a raw GitHub response body.
        private static string DescribeRepositoryFailure(GitHubRepositoryFailureKind? kind) => kind switch
        {
            GitHubRepositoryFailureKind.NotFound => "Repository bulunamadı.",
            GitHubRepositoryFailureKind.Unauthorized => "Oturumunuz geçersiz, lütfen tekrar giriş yapın.",
            GitHubRepositoryFailureKind.RateLimited => "GitHub istek sınırına ulaşıldı, biraz sonra tekrar deneyin.",
            GitHubRepositoryFailureKind.NetworkError => "GitHub'a ulaşılamadı.",
            _ => "Repository bilgileri alınamadı."
        };

        private void SetRepositoryLoading()
        {
            RepositoryErrorLabel.IsVisible = false;
            RepositoryCard.IsVisible = false;
            RepositoryLoadingIndicator.IsVisible = true;
            RepositoryLoadingIndicator.IsRunning = true;
        }

        private void SetRepositoryError(string message)
        {
            RepositoryLoadingIndicator.IsRunning = false;
            RepositoryLoadingIndicator.IsVisible = false;
            RepositoryCard.IsVisible = false;
            RepositoryErrorLabel.Text = message;
            RepositoryErrorLabel.IsVisible = true;
            SemanticScreenReader.Announce(message);
        }

        private void SetRepositoryResult(GitHubRepository repository)
        {
            RepositoryLoadingIndicator.IsRunning = false;
            RepositoryLoadingIndicator.IsVisible = false;
            RepositoryErrorLabel.IsVisible = false;

            RepositoryFullNameLabel.Text = repository.FullName;
            RepositoryDescriptionLabel.Text = string.IsNullOrWhiteSpace(repository.Description)
                ? "Açıklama yok."
                : repository.Description;
            RepositoryStatsLabel.Text = $"{repository.Stars} yıldız · {repository.Forks} fork · {repository.OpenIssues} açık issue";
            RepositoryLanguageLabel.Text = string.IsNullOrEmpty(repository.PrimaryLanguage)
                ? "Ana dil belirtilmemiş"
                : $"Ana dil: {repository.PrimaryLanguage}";
            RepositoryUpdatedLabel.Text = repository.UpdatedAt is { } updatedAt
                ? $"Son güncelleme: {updatedAt.ToLocalTime():dd.MM.yyyy}"
                : "Son güncelleme bilgisi yok";

            var badges = new List<string>();
            if (repository.IsArchived)
            {
                badges.Add("Arşivlenmiş");
            }
            if (repository.IsFork)
            {
                badges.Add("Fork");
            }

            RepositoryBadgesLabel.Text = string.Join(" · ", badges);
            RepositoryBadgesLabel.IsVisible = badges.Count > 0;

            RepositoryCard.IsVisible = true;
        }

        private void ClearRepositoryResult()
        {
            RepositoryLoadingIndicator.IsRunning = false;
            RepositoryLoadingIndicator.IsVisible = false;
            RepositoryErrorLabel.IsVisible = false;
            RepositoryCard.IsVisible = false;
        }
    }
}
