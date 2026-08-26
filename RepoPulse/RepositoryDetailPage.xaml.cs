using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse;

// Receives the already-fetched GitHubRepository via Shell query parameters
// (IQueryAttributable) — never re-fetches, never receives or needs the
// access token (RP-007). Back navigation is Shell's default relative pop,
// which returns to RepositoryListPage.
public partial class RepositoryDetailPage : ContentPage, IQueryAttributable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // RP-012: the exact same DI singleton RepositoryListPage uses — toggling
    // here is immediately visible back on the list without any extra sync
    // step, and vice versa.
    private readonly FavoriteToggleController favoriteToggleController;
    private GitHubRepository? currentRepository;

    public RepositoryDetailPage(FavoriteToggleController favoriteToggleController)
    {
        InitializeComponent();
        this.favoriteToggleController = favoriteToggleController;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(AppRoutes.RepositoryQueryKey, out var value) && value is GitHubRepository repository)
        {
            Render(repository);
        }
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
}
