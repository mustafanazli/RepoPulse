namespace RepoPulse.Core.Repositories;

// Precomputed, MAUI-independent display fields for one row of
// RepositoryListPage's CollectionView (RP-010) — kept separate from
// GitHubRepository itself so the domain model stays free of UI-formatting
// concerns, and so the formatting (including the null/empty fallbacks) is
// unit-testable without a MAUI host. Language/updated/badge/stats phrasing
// intentionally mirrors RepositoryDetailPage's existing wording for
// consistency across screens.
public sealed record RepositoryListItem(
    string FullName,
    string? Description,
    bool HasDescription,
    string StatsText,
    string LanguageText,
    string UpdatedText,
    string? BadgeText,
    bool HasBadge,
    GitHubRepository Repository,
    // RP-012: whether this repository is currently a favorite, plus the
    // Turkish label its toggle button should show. Defaults to false/"add"
    // so every pre-RP-012 call site (production and test) that never passed
    // a value keeps compiling and keeps its previous, correct behavior.
    bool IsFavorite = false,
    string FavoriteToggleLabel = "Favorilere ekle")
{
    public static RepositoryListItem FromRepository(GitHubRepository repository, bool isFavorite = false)
    {
        var languageText = string.IsNullOrEmpty(repository.PrimaryLanguage)
            ? "Ana dil belirtilmemiş"
            : $"Ana dil: {repository.PrimaryLanguage}";

        var updatedText = repository.UpdatedAt is { } updatedAt
            ? $"Son güncelleme: {updatedAt.ToLocalTime():dd.MM.yyyy}"
            : "Son güncelleme bilgisi yok";

        // GitHub's open_issues_count counts open issues AND open pull
        // requests together — labeled accordingly so this isn't read as an
        // issues-only count (same phrasing as RepositoryListPage's single
        // search result and RepositoryDetailPage).
        var statsText = $"{repository.Stars} yıldız · {repository.Forks} fork · {repository.OpenIssuesAndPullRequests} açık issue + PR";

        var badges = new List<string>();
        if (repository.IsArchived)
        {
            badges.Add("Arşivlenmiş");
        }
        if (repository.IsFork)
        {
            badges.Add("Fork");
        }
        var badgeText = badges.Count > 0 ? string.Join(" · ", badges) : null;

        return new RepositoryListItem(
            repository.FullName,
            repository.Description,
            !string.IsNullOrWhiteSpace(repository.Description),
            statsText,
            languageText,
            updatedText,
            badgeText,
            badgeText is not null,
            repository,
            isFavorite,
            isFavorite ? "Favorilerden çıkar" : "Favorilere ekle");
    }
}
