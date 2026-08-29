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
    private readonly FavoriteToggleController favoriteToggleController;

    private bool isRepositoryLookupInProgress;
    private bool isNavigatingToDetail;
    private bool isFetchingFavoriteDetail;
    private GitHubRepository? lastFetchedRepository;

    // RP-012: false = "Tümü" (the live GitHub list, RP-011 behavior
    // unchanged), true = "Favoriler" (the SQLite-backed favorite set).
    // Page-local, ephemeral UI state — same reasoning as the RP-011 fields
    // below it.
    private bool repositoryViewIsFavoritesOnly;

    // RP-011: client-side-only search/sort state over whatever
    // repositoryListController.State.Repositories currently holds. Kept as
    // page-local fields (never on the controller/State) so RP-010's session-
    // generation reload guard and error/offline handling stay entirely
    // untouched; they survive navigating to RepositoryDetailPage and back
    // because Shell keeps this page instance alive, and are re-applied to
    // whatever list a later reload (e.g. a new session generation) produces.
    private IReadOnlyList<GitHubRepository> latestRepositories = Array.Empty<GitHubRepository>();
    private string repositoryListSearchText = string.Empty;
    private RepositorySortOrder repositoryListSortOrder = RepositorySortOrder.UpdatedDescending;

    private CancellationTokenSource? repositoryListLoadCts;
    // Set right before this page itself cancels an in-flight list load
    // (OnDisappearing) — distinguishes "the page is navigating away" from a
    // genuine request timeout, since both surface as the same
    // OperationCanceledException. Only the former must never be shown to
    // the user as an error.
    private bool repositoryListLoadCancelledByNavigation;

    public ObservableCollection<RepositoryListItem> RepositoryItems { get; } = new();

    // RP-012: holds a mix of RepositoryListItem (favorite present in
    // latestRepositories) and FavoriteIdentityRow (favorite not currently
    // live) — see RepositoryListRowTemplateSelector. Only ever populated by
    // RepositoryListItemSynchronizer's generic overload, so it inherits the
    // exact same never-Clear()/never-Reset guarantee as RepositoryItems.
    public ObservableCollection<object> FavoriteRows { get; } = new();

    public RepositoryListPage(
        IGitHubApiClient gitHubApiClient,
        UserSessionStore userSessionStore,
        SessionPersistenceStore sessionPersistenceStore,
        FavoriteToggleController favoriteToggleController)
    {
        InitializeComponent();
        this.gitHubApiClient = gitHubApiClient;
        this.userSessionStore = userSessionStore;
        this.sessionPersistenceStore = sessionPersistenceStore;
        this.favoriteToggleController = favoriteToggleController;
        repositoryListController = new RepositoryListController(gitHubApiClient);

        RepositoryCollectionView.ItemsSource = RepositoryItems;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // A fresh arrival at the list (e.g. after signing back in) should
        // never show a stale result from a previous session.
        isNavigatingToDetail = false;

        // RP-012 (fixed): favorites are scoped to the signed-in GitHub
        // account (UserSessionStore.SessionGeneration) — this reloads them
        // whenever that generation has changed since the last successful
        // load (first appearance, sign-out, switching accounts, or signing
        // back into the same account) and is a cheap no-op otherwise, so
        // calling it on every OnAppearing is correct and inexpensive.
        _ = EnsureFavoritesLoadedAsync();

        var accessToken = userSessionStore.Current?.AccessToken;
        var sessionGeneration = userSessionStore.SessionGeneration;
        if (accessToken is not null && !repositoryListController.IsLoading && !repositoryListController.HasLoadedFor(sessionGeneration))
        {
            _ = LoadRepositoryListAsync(accessToken, sessionGeneration);
        }
        else
        {
            // Returning here without a fresh HTTP reload (e.g. back from
            // RepositoryDetailPage) still needs a re-render: a favorite may
            // have been toggled on the page just left, and
            // FavoriteToggleController is the same shared singleton, so that
            // change is already in memory — it just hasn't been projected
            // into RepositoryItems/FavoriteRows yet.
            ApplyRepositoryListProjection();
        }
    }

    private async Task EnsureFavoritesLoadedAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            await favoriteToggleController.EnsureLoadedForCurrentSessionAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Best-effort: every row simply shows as "not favorited" until
            // the next successful load — this never blocks or fails the
            // repository list itself.
        }

        ApplyRepositoryListProjection();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Never leaves an HTTP request running once the user has navigated
        // away from this page (e.g. into RepositoryDetailPage).
        repositoryListLoadCancelledByNavigation = true;
        repositoryListLoadCts?.Cancel();
    }

    private async Task LoadRepositoryListAsync(string accessToken, long sessionGeneration)
    {
        repositoryListLoadCancelledByNavigation = false;
        SetRepositoryListLoading();

        repositoryListLoadCts?.Dispose();
        repositoryListLoadCts = new CancellationTokenSource(RequestTimeout);

        try
        {
            await repositoryListController.LoadAsync(accessToken, sessionGeneration, repositoryListLoadCts.Token);
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
        latestRepositories = state.Repositories;

        RepositoryListTruncatedBanner.IsVisible = state.IsTruncated && state.Repositories.Count > 0;

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

        ApplyRepositoryListProjection();
    }

    // RP-011: re-derives what the CollectionView actually shows from
    // latestRepositories + the current search text/sort order — never
    // touches repositoryListController or issues a network request. Called
    // both after every controller-driven render (RenderRepositoryListState)
    // and directly from the search/sort UI handlers below.
    private void ApplyRepositoryListProjection()
    {
        var projected = RepositoryListProjection.Apply(latestRepositories, repositoryListSearchText, repositoryListSortOrder);
        var desired = projected
            .Select(repository => RepositoryListItem.FromRepository(repository, favoriteToggleController.IsFavorite(repository.Owner, repository.Name)))
            .ToList();

        // RepositoryListItemSynchronizer (RepoPulse.Core, MAUI-independent
        // and unit-tested) never Clear()s RepositoryItems — only Remove/
        // Insert/Move/indexer-replace — and compares repository identity by
        // FullName case-insensitively, so a casing-only change updates the
        // existing row in place instead of removing+reinserting it or
        // leaving a duplicate behind. See its own doc comment for why a
        // Reset specifically must never happen here. A favorite toggle is
        // just another data change on an already-present row — it goes
        // through this exact same indexer-replace path, never Clear/Reset.
        RepositoryListItemSynchronizer.Sync(RepositoryItems, desired);

        var desiredFavoriteRows = BuildFavoriteRows();
        RepositoryListItemSynchronizer.Sync(FavoriteRows, desiredFavoriteRows, FavoriteRowKey);

        // "No matches" only makes sense when the underlying list genuinely
        // has repositories but the search text filtered all of them out —
        // a truly empty account (RepositoryListStatus.Empty) keeps showing
        // RepositoryListEmptyLabel instead, and error/loading states keep
        // their own messaging untouched by this. Which count decides
        // visibility depends on which view is currently active.
        var activeViewIsEmpty = repositoryViewIsFavoritesOnly ? desiredFavoriteRows.Count == 0 : projected.Count == 0;
        RepositoryListNoMatchesLabel.IsVisible =
            repositoryListController.State.Status == RepositoryListStatus.Loaded && activeViewIsEmpty;
    }

    // RP-012: the actual combine-favorites-with-live-list logic lives in
    // RepoPulse.Core's FavoriteRowProjection (MAUI-independent, unit-tested)
    // — this is just the page wiring its current inputs into it, same
    // pattern as ApplyRepositoryListProjection delegating to
    // RepositoryListProjection.Apply for "Tümü".
    private IReadOnlyList<object> BuildFavoriteRows() =>
        FavoriteRowProjection.Apply(latestRepositories, favoriteToggleController.Favorites, repositoryListSearchText);

    private static string FavoriteRowKey(object row) => row switch
    {
        RepositoryListItem item => FavoriteRepositoryIdentifier.NormalizeFullName(item.FullName),
        FavoriteIdentityRow identity => identity.NormalizedFullName,
        _ => throw new InvalidOperationException($"Unknown favorite row type: {row.GetType()}.")
    };

    private void OnRepositoryListSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        repositoryListSearchText = e.NewTextValue ?? string.Empty;
        ApplyRepositoryListProjection();
    }

    private void OnRepositorySortOrderChanged(object? sender, EventArgs e)
    {
        repositoryListSortOrder = RepositorySortPicker.SelectedIndex switch
        {
            1 => RepositorySortOrder.NameAscending,
            _ => RepositorySortOrder.UpdatedDescending
        };
        ApplyRepositoryListProjection();
    }

    // RP-012: both collections (RepositoryItems/FavoriteRows) are always
    // kept current by ApplyRepositoryListProjection regardless of which is
    // visible, so switching views is just an ItemsSource swap — a
    // deliberate, discrete view change the user asked for by picking a
    // different filter, not a keystroke the SearchBar-focus fix has to
    // protect (unlike the search/sort re-renders, losing focus here is
    // expected).
    private void OnRepositoryViewFilterChanged(object? sender, EventArgs e)
    {
        repositoryViewIsFavoritesOnly = RepositoryViewFilterPicker.SelectedIndex == 1;
        RepositoryCollectionView.ItemsSource = repositoryViewIsFavoritesOnly ? FavoriteRows : RepositoryItems;
        ApplyRepositoryListProjection();
    }

    private async void OnRepositoryItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection.FirstOrDefault();

        // Clear immediately so the same item can be selected again after
        // returning from RepositoryDetailPage, and so a failed/cancelled
        // navigation doesn't leave a stale visual selection behind.
        RepositoryCollectionView.SelectedItem = null;

        switch (selected)
        {
            case RepositoryListItem item:
                await NavigateToDetailAsync(item.Repository);
                break;
            case FavoriteIdentityRow identity:
                await OpenFavoriteIdentityRowAsync(identity);
                break;
        }
    }

    // RP-012: an identity-only favorite row (owner/name/AddedAtUtc only —
    // never a cached GitHubRepository) has nothing to navigate with, so
    // opening its detail means fetching it live first, exactly like the
    // existing single-repository "GitHub'da Repository Aç" lookup above.
    // Any failure (offline, rate-limited, since-deleted repo, ...) shows a
    // short non-blocking message instead of crashing or silently doing
    // nothing.
    private async Task OpenFavoriteIdentityRowAsync(FavoriteIdentityRow identity)
    {
        if (isFetchingFavoriteDetail || isNavigatingToDetail)
        {
            return;
        }

        var accessToken = userSessionStore.Current?.AccessToken;
        if (accessToken is null)
        {
            ShowFavoriteToggleError("Ayrıntıları görmek için önce giriş yapmalısınız.");
            return;
        }

        isFetchingFavoriteDetail = true;
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            GitHubRepositoryResult result;
            try
            {
                result = await gitHubApiClient.GetRepositoryAsync(accessToken, identity.Owner, identity.Name, cts.Token);
            }
            catch (OperationCanceledException)
            {
                ShowFavoriteToggleError(DescribeRepositoryFailure(GitHubRepositoryFailureKind.NetworkError));
                return;
            }

            if (result.FailureKind == GitHubRepositoryFailureKind.Unauthorized)
            {
                await HandleInvalidSessionAsync();
                return;
            }

            if (!result.IsSuccess || result.Repository is null)
            {
                ShowFavoriteToggleError(DescribeRepositoryFailure(result.FailureKind));
                return;
            }

            FavoriteToggleErrorLabel.IsVisible = false;
            await NavigateToDetailAsync(result.Repository);
        }
        finally
        {
            isFetchingFavoriteDetail = false;
        }
    }

    // RP-012: shared by the favorite-toggle failure path and the offline-
    // identity-row-open failure path — always a fixed, safe Turkish
    // message, never the underlying exception/store failure kind, and never
    // hides the repository list itself (unlike SetRepositoryListError).
    private void ShowFavoriteToggleError(string message)
    {
        FavoriteToggleErrorLabel.Text = message;
        FavoriteToggleErrorLabel.IsVisible = true;
        SemanticScreenReader.Announce(message);
    }

    // RP-012: shared Clicked handler for the favorite toggle Button in both
    // item templates (RepositoryListItem and FavoriteIdentityRow) — routed
    // purely through the clicked element's BindingContext, so one handler
    // covers "Tümü", "Favoriler" live rows, and "Favoriler" identity-only
    // rows alike. FavoriteToggleController.ToggleAsync itself guards a fast
    // double-tap on the same identity (returns Ignored, no second DB write);
    // this handler only needs to ignore that outcome.
    private async void OnFavoriteToggleClicked(object? sender, EventArgs e)
    {
        if (sender is not Element { BindingContext: { } bindingContext })
        {
            return;
        }

        string owner;
        string name;
        switch (bindingContext)
        {
            case RepositoryListItem item:
                owner = item.Repository.Owner;
                name = item.Repository.Name;
                break;
            case FavoriteIdentityRow identity:
                owner = identity.Owner;
                name = identity.Name;
                break;
            default:
                return;
        }

        FavoriteToggleErrorLabel.IsVisible = false;

        FavoriteToggleResult result;
        using (var cts = new CancellationTokenSource(RequestTimeout))
        {
            try
            {
                result = await favoriteToggleController.ToggleAsync(owner, name, cts.Token);
            }
            catch (OperationCanceledException)
            {
                result = FavoriteToggleResult.Failure(FavoriteStoreFailureKind.Unexpected);
            }
        }

        if (result.IsIgnored)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ShowFavoriteToggleError("Favori işlemi tamamlanamadı, lütfen tekrar deneyin.");
            return;
        }

        ApplyRepositoryListProjection();
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

        try
        {
            GitHubRepositoryResult repositoryResult;
            using (var cts = new CancellationTokenSource(RequestTimeout))
            {
                try
                {
                    repositoryResult = await gitHubApiClient.GetRepositoryAsync(
                        accessToken,
                        parseResult.Value.Owner,
                        parseResult.Value.Name,
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // GetRepositoryAsync deliberately rethrows rather than
                    // swallows an OperationCanceledException attributable to
                    // this call's own request-timeout token (see its doc
                    // comment) — a genuinely slow/degraded connection, not
                    // an instant refusal, must still never escape this
                    // async void handler uncaught (RP-014).
                    repositoryResult = GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.NetworkError);
                }
            }

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
        finally
        {
            isRepositoryLookupInProgress = false;
            LookupButton.IsEnabled = true;
        }
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
        RepositoryListNoMatchesLabel.IsVisible = false;
        RepositoryListErrorLabel.IsVisible = false;
        RepositoryListLoadingIndicator.IsVisible = true;
        RepositoryListLoadingIndicator.IsRunning = true;
        SetRepositoryListControlsEnabled(false);
    }

    private void SetRepositoryListLoaded()
    {
        RepositoryListLoadingIndicator.IsRunning = false;
        RepositoryListLoadingIndicator.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = false;
        RepositoryListErrorLabel.IsVisible = false;
        SetRepositoryListControlsEnabled(true);
    }

    private void SetRepositoryListEmpty()
    {
        RepositoryListLoadingIndicator.IsRunning = false;
        RepositoryListLoadingIndicator.IsVisible = false;
        RepositoryListErrorLabel.IsVisible = false;
        RepositoryListNoMatchesLabel.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = true;
        SetRepositoryListControlsEnabled(true);
    }

    private void SetRepositoryListError(string message)
    {
        RepositoryListLoadingIndicator.IsRunning = false;
        RepositoryListLoadingIndicator.IsVisible = false;
        RepositoryListEmptyLabel.IsVisible = false;
        RepositoryListNoMatchesLabel.IsVisible = false;
        RepositoryListErrorLabel.Text = message;
        RepositoryListErrorLabel.IsVisible = true;
        SemanticScreenReader.Announce(message);
        SetRepositoryListControlsEnabled(true);
    }

    // RP-011: the search/sort controls operate purely in-memory, but
    // disabling them while a load is in flight keeps their state legible —
    // e.g. avoids a user typing into a search box whose underlying list is
    // about to be replaced by RenderRepositoryListState.
    private void SetRepositoryListControlsEnabled(bool enabled)
    {
        RepositoryListSearchBar.IsEnabled = enabled;
        RepositorySortPicker.IsEnabled = enabled;
        RepositoryViewFilterPicker.IsEnabled = enabled;
    }
}
