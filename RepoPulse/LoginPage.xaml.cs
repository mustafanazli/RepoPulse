using RepoPulse.Authentication;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;

namespace RepoPulse;

// Owns the OAuth Authorization Code + PKCE flow (RP-002/003/005) and the
// live-staging exchange (RP-006-era staging integration). On a successful
// sign-in, persists the session via SessionPersistenceStore (RP-008 —
// SecureStorage, populating UserSessionStore only once that succeeds) and
// replaces the ENTIRE navigation stack with RepositoryListPage (absolute
// "//" route) — so the back button/gesture can never return here to a
// still-pending or already-used sign-in attempt. This page itself is never
// a protected route.
public partial class LoginPage : ContentPage
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly IRepoPulseAuthApiClient authApiClient;
    private readonly IGitHubApiClient gitHubApiClient;
    private readonly AuthorizationSessionStore sessionStore;
    private readonly SessionPersistenceStore sessionPersistenceStore;

    private bool isSubscribedToOAuthCallbacks;
    private bool isSignInInProgress;

    // RP-014: identifies the current attempt to OAuthLoginAttemptCoordinator
    // — never a token/code/state/verifier, just a monotonic id. 0 means no
    // attempt has been started yet on this page instance.
    private long currentAttemptId;

    public LoginPage(
        IRepoPulseAuthApiClient authApiClient,
        IGitHubApiClient gitHubApiClient,
        AuthorizationSessionStore sessionStore,
        SessionPersistenceStore sessionPersistenceStore)
    {
        InitializeComponent();
        this.authApiClient = authApiClient;
        this.gitHubApiClient = gitHubApiClient;
        this.sessionStore = sessionStore;
        this.sessionPersistenceStore = sessionPersistenceStore;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!isSubscribedToOAuthCallbacks)
        {
            OAuthCallbackBroker.CallbackReceived += OnOAuthCallbackReceived;
            OAuthCallbackBroker.AttemptAbandoned += OnAttemptAbandoned;
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
            OAuthCallbackBroker.AttemptAbandoned -= OnAttemptAbandoned;
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
        SetStatus("GitHub'a yönlendiriliyor...");

        // RP-014: recorded right before the system browser takes over, so
        // MainActivity.OnResume can tell "the browser came back with no
        // callback" apart from every other reason it might resume.
        currentAttemptId = OAuthCallbackBroker.AttemptCoordinator.StartAttempt();

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
    //
    // RP-014 follow-up audit: the callback itself never carries an attempt id
    // (GitHub's redirect only contains code/state/error) — currentAttemptId
    // below is always THIS page instance's own field, which a stale callback
    // from an already-abandoned earlier attempt cannot distinguish itself
    // from. OAuthCallbackAttemptGate is what makes this safe: it consults
    // AuthorizationSessionStore FIRST, so the coordinator is only ever told
    // "the callback won" once the callback's own state is proven to match the
    // session we are currently actually waiting on. See its doc comment.
    private async void OnOAuthCallbackReceived(object? sender, OAuthCallbackResult result)
    {
        var decision = OAuthCallbackAttemptGate.Evaluate(
            result,
            sessionStore,
            OAuthCallbackBroker.AttemptCoordinator,
            currentAttemptId,
            out var validatedSession);

        switch (decision)
        {
            case OAuthCallbackDecision.ProceedWithExchange:
                await HandleSuccessfulCallbackAsync(result.Code!, validatedSession!);
                break;

            case OAuthCallbackDecision.AttemptEndedSafely:
                SetStatus(result.Outcome == OAuthCallbackOutcome.Cancelled
                    ? "Giriş iptal edildi."
                    : "Giriş isteği doğrulanamadı, lütfen tekrar deneyin.");
                EndSignInAttempt();
                break;

            case OAuthCallbackDecision.Ignored:
            default:
                // Did not belong to the currently active attempt (wrong/
                // expired state, or that attempt already concluded by other
                // means) — silently no-op so whichever attempt is genuinely
                // active, if any, continues completely undisturbed.
                break;
        }
    }

    // RP-014: fires when MainActivity.OnResume observes that the current
    // attempt was abandoned — the system browser took over the foreground
    // at least once and we are back with no callback ever having arrived
    // (offline device, or the user backed out). Resets the page to a fully
    // usable state without requiring an app restart, and clears the
    // now-meaningless pending PKCE session so an immediate retry succeeds.
    private void OnAttemptAbandoned()
    {
        sessionStore.Reset();
        EndSignInAttempt();
        MainThread.BeginInvokeOnMainThread(() => StatusLabel.IsVisible = false);
    }

    // `session` has already been validated by OAuthCallbackAttemptGate (via
    // AuthorizationSessionStore.TryConsume) before this is ever called — never
    // re-validated or re-consumed here.
    private async Task HandleSuccessfulCallbackAsync(string code, AuthorizationSession session)
    {
        SetStatus("Doğrulanıyor...");

        using var cts = new CancellationTokenSource(RequestTimeout);

        AuthApiExchangeResult exchangeResult;
        GitHubUserResult userResult;
        try
        {
            exchangeResult = await authApiClient.ExchangeAsync(code, session.CodeVerifier, cts.Token);
            if (!exchangeResult.IsSuccess || exchangeResult.Success is null)
            {
                SetStatus(DescribeExchangeFailure(exchangeResult.FailureKind));
                EndSignInAttempt();
                return;
            }

            userResult = await gitHubApiClient.GetCurrentUserAsync(exchangeResult.Success.AccessToken, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // ExchangeAsync/GetCurrentUserAsync deliberately rethrow rather
            // than swallow an OperationCanceledException attributable to
            // this call's own request-timeout token (see their doc
            // comments) — a genuinely slow/degraded connection, not an
            // instant refusal, must still never escape this async void
            // handler uncaught (RP-014).
            SetStatus("İstek zaman aşımına uğradı, lütfen tekrar deneyin.");
            EndSignInAttempt();
            return;
        }

        var accessToken = exchangeResult.Success.AccessToken;
        var refreshToken = exchangeResult.Success.RefreshToken;

        if (!userResult.IsSuccess || userResult.User is null)
        {
            SetStatus($"Giriş başarısız: {userResult.SafeErrorMessage}");
            EndSignInAttempt();
            return;
        }

        var accessTokenExpiresAtUtc = exchangeResult.Success.ExpiresIn is { } expiresInSeconds
            ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
            : (DateTimeOffset?)null;

        var userSession = new UserSession(accessToken, refreshToken, userResult.User.Login, userResult.User.AvatarUrl, accessTokenExpiresAtUtc);

        // A sign-in is not considered complete until the session is
        // durably persisted — SessionPersistenceStore only populates the
        // in-memory UserSessionStore once SecureStorage write succeeds.
        var persisted = await sessionPersistenceStore.SignInAsync(userSession, cts.Token);
        if (!persisted)
        {
            SetStatus("Oturum güvenli şekilde kaydedilemedi, lütfen tekrar deneyin.");
            EndSignInAttempt();
            return;
        }

        EndSignInAttempt();
        await NavigateToRepositoryListAsync();
    }

    private async Task NavigateToRepositoryListAsync()
    {
        try
        {
            // Absolute route ("//") replaces the whole navigation stack —
            // the back button/gesture can never return here afterwards.
            await Shell.Current.GoToAsync($"//{AppRoutes.RepositoryList}");
        }
        catch (Exception)
        {
            // Never show a raw exception message — Shell navigation
            // failures are rare and not user-actionable beyond retrying.
            SetStatus("Bir şeyler ters gitti, lütfen tekrar deneyin.");
        }
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
            StatusLabel.Text = statusText;
            StatusLabel.IsVisible = true;
        });
}
