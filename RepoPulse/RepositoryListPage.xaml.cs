using System.Collections.ObjectModel;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse;

// RP-006's single repository lookup (now the page's "GitHub'da Repository
// Aç" section) plus RP-010's real repository list, both reading the access
// token from UserSessionStore (never from a route/query parameter). List
// loading/error/empty/truncation state is owned by RepositoryListController
// (RepoPulse.Core, MAUI-independent and unit-testable); this page only
// drives it and renders RepositoryListState. Selecting either a list item or
// the single search result offers navigation to RepositoryDetailPage —
// passing only the already-fetched GitHubRepository object, never the
// token, via Shell query parameters (RP-007). A 401 from GitHub (e.g. a
// restored-but-now-invalid RP-008 session), from either the list load or the
// single lookup, clears both the persisted and in-memory session and
// returns to Login.
public partial class RepositoryListPage : ContentPage
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly IGitHubApiClient gitHubApiClient;
    private readonly UserSessionStore userSessionStore;
    private readonly SessionPersistenceStore sessionPersistenceStore;
    private readonly RepositoryListController repositoryListController;

    private bool isRepositoryLookupInProgress;
    private bool isNavigatingToDetail;
    private GitHubRepository? lastFetchedRepository;

    private CancellationTokenSource? repositoryListLoadCts;
    // Set right before this page itself cancels an in-flight list load
    // (OnDisappearing) — distinguishes "the page is navigating away" from a
    // genuine request timeout, since both surface as the same
    // OperationCanceledException. Only the former must never be shown to
    // the user as an error.
    private bool repositoryListLoadCancelledByNavigation;

    public ObservableCollection<RepositoryListItem> RepositoryItems { get; } = new();

    public RepositoryListPage(IGitHubApiClient gitHubApiClient, UserSessionStore userSessionStore, SessionPersistenceStore sessionPersistenceStore)
    {
        InitializeComponent();
        this.gitHubApiClient = gitHubApiClient;
        this.userSessionStore = userSessionStore;
        this.sessionPersistenceStore = sessionPersistenceStore;
        repositoryListController = new RepositoryListController(gitHubApiClient);

        RepositoryCollectionView.ItemsSource = RepositoryItems;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // A fresh arrival at the list (e.g. after signing back in) should
        // never show a stale result from a previous session.
        isNavigatingToDetail = false;

        var accessToken = userSessionStore.Current?.AccessToken;
        if (accessToken is not null && !repositoryListController.IsLoading && !repositoryListController.HasLoadedFor(accessToken))
        {
            _ = LoadRepositoryListAsync(accessToken);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Never leaves an HTTP request running once the user has navigated
        // away from this page (e.g. into RepositoryDetailPage).
        repositoryListLoadCancelledByNavigation = true;
        repositoryListLoadCts?.Cancel();
    }

    private async Task LoadRepositoryListAsync(string accessToken)
    {
        repositoryListLoadCancelledByNavigation = false;
        SetRepositoryListLoading();

        repositoryListLoadCts?.Dispose();
        repositoryListLoadCts = new CancellationTokenSource(RequestTimeout);

        try
        {
            await repositoryListController.LoadAsync(accessToken, repositoryListLoadCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (repositoryListLoadCancelledByNavigation)
            {
                // The page is going away — never show cancellation as an
                // error; OnAppearing will simply retry next time the list
                // hasn't successfully loaded yet.
                return;
            }

            // A genuine timeout, not a navigation-triggered cancellation —
            // this is a real connectivity problem and must be reported.
            SetRepositoryListError(DescribeRepositoryFailure(GitHubRepositoryFailureKind.NetworkError));
            return;
        }

        RenderRepositoryListState();

        if (repositoryListController.State.Status == RepositoryListStatus.Unauthorized)
        {
            await HandleInvalidSessionAsync();
        }
    }

    private void RenderRepositoryListState()
    {
        var state = repositoryListController.State;

        RepositoryItems.Clear();
        foreach (var repository in state.Repositories)
        {
            RepositoryItems.Add(RepositoryListItem.FromRepository(repository));
        }

        RepositoryListTruncatedBanner.IsVisible = state.IsTruncated && RepositoryItems.Count > 0;

        switch (state.Status)
        {
            case RepositoryListStatus.Loaded:
                SetRepositoryListLoaded();
                break;
            case RepositoryListStatus.Empty:
                SetRepositoryListEmpty();
                break;
            case RepositoryListStatus.Unauthorized:
                // No error text needed here — HandleInvalidSessionAsync
                // (called by the caller right after this) navigates to
                // Login immediately.
                break;
            case RepositoryListStatus.RateLimited:
                SetRepositoryListError(DescribeRepositoryFailure(GitHubRepositoryFailureKind.RateLimited));
                break;
            case RepositoryListStatus.NetworkError:
                SetRepositoryListError(DescribeRepositoryFailure(GitHubRepositoryFailureKind.NetworkError));
                break;
            default:
                SetRepositoryListError(DescribeRepositoryFailure(null));
                break;
        }
    }

    private async void OnRepositoryItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection.FirstOrDefault() as RepositoryListItem;

        // Clear immediately so the same item can be selected again after
        // returning from RepositoryDetailPage, and so a failed/cancelled
        // navigation doesn't leave a stale visual selection behind.
        RepositoryCollectionView.SelectedItem = null;

        if (selected is null)
        {
            return;
        }

        await NavigateToDetailAsync(selected.Repository);
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
        if (lastFetchedRepository is null)
        {
            return;
        }

        await NavigateToDetailAsync(lastFetchedRepository);
    }

    // Shared by both entry points (a tapped list item and the single-search
    // "Detayları Gör" button) — one guard, so tapping both in quick
    // succession still only ever navigates once.
    private async Task NavigateToDetailAsync(GitHubRepository repository)
    {
        if (isNavigatingToDetail)
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
            var query = RepositoryNavigationQueryBuilder.Build(repository);
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
    // raw GitHub response body. Shared by the single search AND the
    // repository list, since both surface the same GitHubRepositoryFailureKind
    // values (the list never produces NotFound).
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

    private void SetRepositoryListLoading()
    {
        RepositoryListTruncatedBanner.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = false;
        RepositoryListErrorLabel.IsVisible = false;
        RepositoryListLoadingIndicator.IsVisible = true;
        RepositoryListLoadingIndicator.IsRunning = true;
    }

    private void SetRepositoryListLoaded()
    {
        RepositoryListLoadingIndicator.IsRunning = false;
        RepositoryListLoadingIndicator.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = false;
        RepositoryListErrorLabel.IsVisible = false;
    }

    private void SetRepositoryListEmpty()
    {
        RepositoryListLoadingIndicator.IsRunning = false;
        RepositoryListLoadingIndicator.IsVisible = false;
        RepositoryListErrorLabel.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = true;
    }

    private void SetRepositoryListError(string message)
    {
        RepositoryListLoadingIndicator.IsRunning = false;
        RepositoryListLoadingIndicator.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = false;
        RepositoryListErrorLabel.Text = message;
        RepositoryListErrorLabel.IsVisible = true;
        SemanticScreenReader.Announce(message);
    }
}
