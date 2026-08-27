namespace RepoPulse.Core.Repositories;

// Typed result models for IFavoriteRepositoryStore. Each keeps "the
// operation succeeded but found nothing" and "the operation failed" as two
// distinct, non-confusable states — e.g. FavoriteListResult.Success with an
// empty list ("no favorites yet") can never be mistaken for
// FavoriteListResult.Failure ("the database could not be read"), which a
// bare IReadOnlyList<FavoriteRepository> (empty on either outcome) could not
// distinguish.
public sealed record FavoriteStoreResult(bool IsSuccess, FavoriteStoreFailureKind? FailureKind)
{
    public static FavoriteStoreResult Success() => new(true, null);

    public static FavoriteStoreResult Failure(FavoriteStoreFailureKind kind) => new(false, kind);
}

public sealed record FavoriteListResult(bool IsSuccess, IReadOnlyList<FavoriteRepository> Favorites, FavoriteStoreFailureKind? FailureKind)
{
    public static FavoriteListResult Success(IReadOnlyList<FavoriteRepository> favorites) => new(true, favorites, null);

    public static FavoriteListResult Failure(FavoriteStoreFailureKind kind) => new(false, Array.Empty<FavoriteRepository>(), kind);
}

public sealed record FavoriteStatusResult(bool IsSuccess, bool IsFavorite, FavoriteStoreFailureKind? FailureKind)
{
    public static FavoriteStatusResult Success(bool isFavorite) => new(true, isFavorite, null);

    public static FavoriteStatusResult Failure(FavoriteStoreFailureKind kind) => new(false, false, kind);
}
