using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse;

// RP-006's repository lookup, moved here from the removed MainPage. Reads
// the access token from UserSessionStore (never from a route/query
// parameter) and, on a successful lookup, offers navigation to
// RepositoryDetailPage — passing only the already-fetched GitHubRepository
// object, never the token, via Shell query parameters (RP-007). A 401 from
// GitHub (e.g. a restored-but-now-invalid RP-008 session) clears both the
// persisted and in-memory session and returns to Login.
public partial class RepositoryListPage : ContentPage
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly IGitHubApiClient gitHubApiClient;
    private readonly UserSessionStore userSessionStore;
    private readonly SessionPersistenceStore sessionPersistenceStore;

    private bool isRepositoryLookupInProgress;
    private bool isNavigatingToDetail;
    private GitHubRepository? lastFetchedRepository;

    public RepositoryListPage(IGitHubApiClient gitHubApiClient, UserSessionStore userSessionStore, SessionPersistenceStore sessionPersistenceStore)
    {
        InitializeComponent();
        this.gitHubApiClient = gitHubApiClient;
        this.userSessionStore = userSessionStore;
        this.sessionPersistenceStore = sessionPersistenceStore;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // A fresh arrival at the list (e.g. after signing back in) should
        // never show a stale result from a previous session.
        isNavigatingToDetail = false;
    }

    private async void OnLookupRepositoryClicked(object? sender, EventArgs e)
    {
        if (isRepositoryLookupInProgress)
        {
            // A lookup is already in flight — a double-tap must never start
            // a second concurrent request.
            return;
        }

        var parseResult = RepositoryIdentifierParser.Parse(RepositoryInput.Text);
        if (!parseResult.IsSuccess || parseResult.Value is null)
        {
            SetRepositoryError(parseResult.SafeErrorMessage ?? "Repository adı geçersiz.");
            return;
        }

        var accessToken = userSessionStore.Current?.AccessToken;
        if (accessToken is null)
        {
            SetRepositoryError("Önce GitHub ile giriş yapmalısınız.");
            return;
        }

        isRepositoryLookupInProgress = true;
        LookupButton.IsEnabled = false;
        SetRepositoryLoading();

        using var cts = new CancellationTokenSource(RequestTimeout);
        var repositoryResult = await gitHubApiClient.GetRepositoryAsync(
            accessToken,
            parseResult.Value.Owner,
            parseResult.Value.Name,
            cts.Token);

        isRepositoryLookupInProgress = false;
        LookupButton.IsEnabled = true;

        if (!repositoryResult.IsSuccess || repositoryResult.Repository is null)
        {
            if (repositoryResult.FailureKind == GitHubRepositoryFailureKind.Unauthorized)
            {
                // A previously-valid (possibly restored, RP-008) token was
                // just rejected by GitHub itself — the session is no
                // longer valid regardless of what SecureStorage still
                // holds. Always route to Login here, even if clearing the
                // persisted copy fails: keeping the user "signed in" to a
                // token GitHub has already rejected is worse than a
                // possible stale value left on disk (which the next
                // restore attempt will simply try to clear again).
                await HandleInvalidSessionAsync();
                return;
            }

            SetRepositoryError(DescribeRepositoryFailure(repositoryResult.FailureKind));
            return;
        }

        SetRepositoryResult(repositoryResult.Repository);
    }

    private async Task HandleInvalidSessionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            await sessionPersistenceStore.SignOutAsync(cts.Token);
        }
        catch (Exception)
        {
        }

        try
        {
            await Shell.Current.GoToAsync($"//{AppRoutes.Login}");
        }
        catch (Exception)
        {
        }
    }

    private async void OnViewDetailClicked(object? sender, EventArgs e)
    {
        if (isNavigatingToDetail || lastFetchedRepository is null)
        {
            return;
        }

        isNavigatingToDetail = true;
        ViewDetailButton.IsEnabled = false;

        try
        {
            // Relative route: pushes onto the current stack, so the Shell
            // back button naturally returns here. Only the repository
            // object travels in the query — never the access token.
            var query = RepositoryNavigationQueryBuilder.Build(lastFetchedRepository);
            await Shell.Current.GoToAsync(AppRoutes.RepositoryDetail, new Dictionary<string, object>(query));
        }
        catch (Exception)
        {
            SetRepositoryError("Detay sayfası açılamadı, lütfen tekrar deneyin.");
        }
        finally
        {
            isNavigatingToDetail = false;
            ViewDetailButton.IsEnabled = true;
        }
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(AppRoutes.Settings);
        }
        catch (Exception)
        {
            // No user-facing surface for a toolbar navigation failure beyond
            // simply not navigating; nothing sensitive to hide here either.
        }
    }

    // Maps the typed failure kind to a short, safe Turkish message — never a
    // raw GitHub response body.
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
        lastFetchedRepository = null;
        RepositoryErrorLabel.IsVisible = false;
        RepositoryCard.IsVisible = false;
        RepositoryLoadingIndicator.IsVisible = true;
        RepositoryLoadingIndicator.IsRunning = true;
    }

    private void SetRepositoryError(string message)
    {
        lastFetchedRepository = null;
        RepositoryLoadingIndicator.IsRunning = false;
        RepositoryLoadingIndicator.IsVisible = false;
        RepositoryCard.IsVisible = false;
        RepositoryErrorLabel.Text = message;
        RepositoryErrorLabel.IsVisible = true;
        SemanticScreenReader.Announce(message);
    }

    private void SetRepositoryResult(GitHubRepository repository)
    {
        lastFetchedRepository = repository;

        RepositoryLoadingIndicator.IsRunning = false;
        RepositoryLoadingIndicator.IsVisible = false;
        RepositoryErrorLabel.IsVisible = false;

        RepositoryFullNameLabel.Text = repository.FullName;
        // GitHub's open_issues_count counts open issues AND open pull
        // requests together — labeled accordingly so this isn't read as an
        // issues-only count.
        RepositoryStatsLabel.Text = $"{repository.Stars} yıldız · {repository.Forks} fork · {repository.OpenIssuesAndPullRequests} açık issue + PR";

        RepositoryCard.IsVisible = true;
    }
}
