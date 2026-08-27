namespace RepoPulse.Core.Repositories;

// Three distinct outcomes of one FavoriteToggleController.ToggleAsync call:
// it actually changed persisted state (Success), it was a fast repeat tap on
// the same identity while the first call was still in flight and did
// nothing (Ignored — never a second DB write), or the underlying store
// failed (Failure). A caller that only checked a bool would not be able to
// tell "nothing happened because it was a duplicate tap" apart from "nothing
// happened because it failed".
public sealed record FavoriteToggleResult(bool IsSuccess, bool IsIgnored, bool IsFavoriteAfterToggle, FavoriteStoreFailureKind? FailureKind)
{
    public static FavoriteToggleResult Success(bool isFavoriteAfterToggle) => new(true, false, isFavoriteAfterToggle, null);

    public static FavoriteToggleResult Ignored() => new(false, true, false, null);

    public static FavoriteToggleResult Failure(FavoriteStoreFailureKind kind) => new(false, false, false, kind);
}
