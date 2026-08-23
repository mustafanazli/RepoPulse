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
    int OpenIssues,
    string? PrimaryLanguage,
    string DefaultBranch,
    bool IsArchived,
    bool IsFork,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PushedAt);
