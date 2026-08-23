namespace RepoPulse.Core.Repositories;

// MAUI-independent GitHub repository summary — only the fields RP-006's
// repository-lookup card actually displays (or the parser/client need to
// round-trip). No health score, no commit history, no pagination metadata.
public sealed record GitHubRepository(
    string Owner,
    string Name,
    string FullName,
    string? Description,
    string HtmlUrl,
    int Stars,
    int Forks,
    // GitHub's own `open_issues_count` field counts open issues AND open
    // pull requests together — the API does not separate them. Named to
    // say so explicitly, rather than implying an issues-only count.
    int OpenIssuesAndPullRequests,
    string? PrimaryLanguage,
    string DefaultBranch,
    bool IsArchived,
    bool IsFork,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PushedAt);
