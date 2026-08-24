using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse;

// Receives the already-fetched GitHubRepository via Shell query parameters
// (IQueryAttributable) — never re-fetches, never receives or needs the
// access token (RP-007). Back navigation is Shell's default relative pop,
// which returns to RepositoryListPage.
public partial class RepositoryDetailPage : ContentPage, IQueryAttributable
{
    public RepositoryDetailPage()
    {
        InitializeComponent();
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
    }
}
