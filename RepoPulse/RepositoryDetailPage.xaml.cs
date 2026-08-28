using RepoPulse.Core.Authentication;
using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse;

// Receives the already-fetched GitHubRepository via Shell query parameters
// (IQueryAttributable) — never re-fetches that summary, and the query
// payload itself never carries the access token (RP-007). RP-013 adds one
// on-page fetch of the repository's latest commit (the summary alone has no
// commit data), reading the access token transiently from UserSessionStore
// exactly like RepositoryListPage does — never copied into a page field.
// Back navigation is Shell's default relative pop, which returns to
// RepositoryListPage.
public partial class RepositoryDetailPage : ContentPage, IQueryAttributable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // RP-012: the exact same DI singleton RepositoryListPage uses — toggling
    // here is immediately visible back on the list without any extra sync
    // step, and vice versa.
    private readonly FavoriteToggleController favoriteToggleController;
    private readonly IGitHubApiClient gitHubApiClient;
    private readonly UserSessionStore userSessionStore;
    private readonly SessionPersistenceStore sessionPersistenceStore;
    private GitHubRepository? currentRepository;

    // RP-013 (hardened): owns which latest-commit load is "current" via an
    // explicit, monotonic operation id (RepoPulse.Core, unit-tested in
    // isolation) rather than a bare bool + a single shared
    // CancellationTokenSource — a superseded operation's own catch/finally,
    // however delayed, can never clear this page's loading flag, cancel a
    // newer operation's token, or overwrite a newer result once a new
    // operation has started. See LatestCommitLoadCoordinator's doc comment.
    private readonly LatestCommitLoadCoordinator latestCommitCoordinator = new();

    public RepositoryDetailPage(
        FavoriteToggleController favoriteToggleController,
        IGitHubApiClient gitHubApiClient,
        UserSessionStore userSessionStore,
        SessionPersistenceStore sessionPersistenceStore)
    {
        InitializeComponent();
        this.favoriteToggleController = favoriteToggleController;
        this.gitHubApiClient = gitHubApiClient;
        this.userSessionStore = userSessionStore;
        this.sessionPersistenceStore = sessionPersistenceStore;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(AppRoutes.RepositoryQueryKey, out var value) && value is GitHubRepository repository)
        {
            Render(repository);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Never leaves an HTTP request running once the user has navigated
        // away from this page.
        latestCommitCoordinator.CancelForNavigation();
    }

    private void Render(GitHubRepository repository)
    {
        currentRepository = repository;
        RepositoryFullNameLabel.Text = repository.FullName;
        RepositoryDescriptionLabel.Text = string.IsNullOrWhiteSpace(repository.Description)
            ? "Açıklama yok."
            : repository.Description;
        // GitHub's open_issues_count counts open issues AND open pull
        // requests together — labeled accordingly so this isn't read as an
        // issues-only count.
        RepositoryStatsLabel.Text = $"{repository.Stars} yıldız · {repository.Forks} fork · {repository.OpenIssuesAndPullRequests} açık issue + PR";
        RepositoryLanguageLabel.Text = string.IsNullOrEmpty(repository.PrimaryLanguage)
            ? "Ana dil belirtilmemiş"
            : $"Ana dil: {repository.PrimaryLanguage}";
        RepositoryDefaultBranchLabel.Text = $"Varsayılan dal: {repository.DefaultBranch}";
        RepositoryUpdatedLabel.Text = repository.UpdatedAt is { } updatedAt
            ? $"Son güncelleme: {updatedAt.ToLocalTime():dd.MM.yyyy}"
            : "Son güncelleme bilgisi yok";
        RepositoryPushedLabel.Text = repository.PushedAt is { } pushedAt
            ? $"Son push: {pushedAt.ToLocalTime():dd.MM.yyyy}"
            : "Son push bilgisi yok";
        RepositoryUrlLabel.Text = repository.HtmlUrl;

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

        FavoriteErrorLabel.IsVisible = false;
        RenderFavoriteState();

        _ = LoadLatestCommitAsync(repository);
    }

    // RP-013: fetches GET /repos/{owner}/{repository}/commits?per_page=1 —
    // the single-repository summary this page is already rendering has no
    // commit data. Guarded against overlap by latestCommitCoordinator; a
    // stale in-flight request from a previous appearance is always
    // cancelled first via CancelForNavigation (OnDisappearing). Every
    // resumption point below re-checks IsCurrent(operationId) immediately
    // before touching any shared UI/session state, so a superseded
    // operation's belated continuation can never win a race against a
    // newer one — see LatestCommitLoadCoordinator's doc comment for why a
    // bare bool + single CancellationTokenSource could not guarantee this.
    private async Task LoadLatestCommitAsync(GitHubRepository repository)
    {
        if (latestCommitCoordinator.HasActiveOperation)
        {
            return;
        }

        var operation = latestCommitCoordinator.StartOperation(RequestTimeout);
        SetLatestCommitLoading();

        try
        {
            // Captured together (same pattern as FavoriteToggleController's
            // cross-session race fix) so a result that resolves after a
            // sign-out/sign-in elsewhere is never applied to this page's
            // labels — the access token itself is read once, right before
            // the call, and never stored in a field.
            var sessionSnapshot = userSessionStore.CaptureSnapshot();
            var accessToken = userSessionStore.Current?.AccessToken;

            if (accessToken is null)
            {
                if (latestCommitCoordinator.IsCurrent(operation.OperationId))
                {
                    SetLatestCommitError(GenericLatestCommitFailureMessage);
                }

                return;
            }

            GitHubLatestCommitResult result;
            try
            {
                result = await gitHubApiClient.GetLatestRepositoryCommitAsync(accessToken, repository.Owner, repository.Name, operation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!latestCommitCoordinator.IsCurrent(operation.OperationId))
                {
                    return;
                }

                if (latestCommitCoordinator.WasCancelledForNavigation(operation.OperationId))
                {
                    return;
                }

                SetLatestCommitError(GenericLatestCommitFailureMessage);
                return;
            }

            if (!latestCommitCoordinator.IsCurrent(operation.OperationId))
            {
                // A newer load has already started (or this page is
                // navigating away) — this result belongs to a superseded
                // operation and must never reach the UI.
                return;
            }

            if (!userSessionStore.IsCurrent(sessionSnapshot))
            {
                // The session changed while the request was in flight —
                // discard rather than render a result fetched under a
                // session that is no longer the active one.
                return;
            }

            if (result.FailureKind == GitHubLatestCommitFailureKind.Unauthorized)
            {
                await HandleInvalidSessionAsync();
                return;
            }

            if (!result.IsSuccess)
            {
                SetLatestCommitError(GenericLatestCommitFailureMessage);
                return;
            }

            if (!result.HasCommits)
            {
                SetLatestCommitNoCommits();
                return;
            }

            SetLatestCommitResult(result.Commit!);
        }
        finally
        {
            latestCommitCoordinator.CompleteOperation(operation.OperationId);
        }
    }

    private const string GenericLatestCommitFailureMessage = "Son commit bilgisi alınamadı.";

    private void SetLatestCommitLoading()
    {
        LatestCommitLabel.Text = "Son commit yükleniyor...";
        LatestCommitLoadingIndicator.IsVisible = true;
        LatestCommitLoadingIndicator.IsRunning = true;
    }

    private void SetLatestCommitResult(GitHubLatestCommit commit)
    {
        LatestCommitLoadingIndicator.IsRunning = false;
        LatestCommitLoadingIndicator.IsVisible = false;
        LatestCommitLabel.Text = $"Son commit: {commit.CommittedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
    }

    private void SetLatestCommitNoCommits()
    {
        LatestCommitLoadingIndicator.IsRunning = false;
        LatestCommitLoadingIndicator.IsVisible = false;
        LatestCommitLabel.Text = "Bu repository'de henüz commit yok.";
    }

    private void SetLatestCommitError(string message)
    {
        LatestCommitLoadingIndicator.IsRunning = false;
        LatestCommitLoadingIndicator.IsVisible = false;
        LatestCommitLabel.Text = message;
        SemanticScreenReader.Announce(message);
    }

    private void RenderFavoriteState()
    {
        if (currentRepository is null)
        {
            return;
        }

        var isFavorite = favoriteToggleController.IsFavorite(currentRepository.Owner, currentRepository.Name);
        FavoriteToggleButton.Text = isFavorite ? "Favorilerden çıkar" : "Favorilere ekle";
    }

    private async void OnFavoriteToggleClicked(object? sender, EventArgs e)
    {
        if (currentRepository is null)
        {
            return;
        }

        FavoriteErrorLabel.IsVisible = false;
        // A fast double-tap here is also guarded at the controller level
        // (ToggleAsync returns Ignored for a repeat call on the same
        // identity while the first is still in flight) — disabling the
        // button too just avoids a pointless second await.
        FavoriteToggleButton.IsEnabled = false;
        try
        {
            FavoriteToggleResult result;
            using (var cts = new CancellationTokenSource(RequestTimeout))
            {
                try
                {
                    result = await favoriteToggleController.ToggleAsync(currentRepository.Owner, currentRepository.Name, cts.Token);
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
                FavoriteErrorLabel.Text = "Favori işlemi tamamlanamadı, lütfen tekrar deneyin.";
                FavoriteErrorLabel.IsVisible = true;
                SemanticScreenReader.Announce(FavoriteErrorLabel.Text);
                return;
            }

            RenderFavoriteState();
        }
        finally
        {
            FavoriteToggleButton.IsEnabled = true;
        }
    }

    // Mirrors RepositoryListPage.HandleInvalidSessionAsync exactly — a
    // previously-valid token was just rejected by GitHub itself, so the
    // session is no longer valid regardless of what SecureStorage still
    // holds.
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
}
