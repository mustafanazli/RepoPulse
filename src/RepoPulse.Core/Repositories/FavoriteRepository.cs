namespace RepoPulse.Core.Repositories;

// RP-012: the persisted identity of one favorited repository — deliberately
// NOT a copy of GitHubRepository. Only what's needed to identify the
// repository again later and to show something meaningful offline (owner,
// name, when it was favorited) is ever written to SQLite; stars,
// description, language, and every other API field are recomputed from a
// live GitHubRepository whenever one is available instead of being cached
// here (see docs/RP-012 scope note: full offline repository cache is
// explicitly out of scope for this RP).
public sealed record FavoriteRepository(
    string Owner,
    string Name,
    string NormalizedFullName,
    DateTimeOffset AddedAtUtc);
