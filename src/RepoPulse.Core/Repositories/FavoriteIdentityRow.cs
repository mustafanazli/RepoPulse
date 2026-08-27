namespace RepoPulse.Core.Repositories;

// RP-012: a "Favoriler" row for a favorite that is NOT currently present in
// latestRepositories (offline, or simply not in this session's loaded
// list) — only what FavoriteRepository itself carries (owner, name, when it
// was added) is ever shown; stars/description/language are never fabricated
// from nothing. RepositoryListPage shows this template's fixed "Ayrıntılar
// için bağlantı gerekli." notice alongside it and fetches the repository
// live (GET /repos/{owner}/{name}) only if the row is actually tapped.
public sealed record FavoriteIdentityRow(
    string NormalizedFullName,
    string Owner,
    string Name,
    string FullName,
    DateTimeOffset AddedAtUtc,
    string AddedAtText)
{
    public const string OfflineNoticeText = "Ayrıntılar için bağlantı gerekli.";

    public static FavoriteIdentityRow FromFavorite(FavoriteRepository favorite) => new(
        favorite.NormalizedFullName,
        favorite.Owner,
        favorite.Name,
        $"{favorite.Owner}/{favorite.Name}",
        favorite.AddedAtUtc,
        $"Eklendi: {favorite.AddedAtUtc.ToLocalTime():dd.MM.yyyy}");
}
