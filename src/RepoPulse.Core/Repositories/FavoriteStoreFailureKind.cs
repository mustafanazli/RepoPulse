namespace RepoPulse.Core.Repositories;

// RP-012 typed store failures — deliberately never carries the raw
// SQLiteException/message/path, so a caller can only ever observe one of
// these four buckets, never anything that could leak a filesystem path or a
// SQL fragment to a log or the UI.
public enum FavoriteStoreFailureKind
{
    // The database file could not be opened/read/written (missing
    // directory, permissions, disk full, locked, or any other I/O-shaped
    // SQLite result) — covers what the RP-012 brief calls "Unavailable".
    IoError,

    // The database file exists but is not a valid SQLite database (or its
    // content is corrupted) — never auto-deleted/recreated by the store;
    // surfaced so the caller can decide what, if anything, to do.
    Corrupt,

    // PRAGMA user_version reports a schema version newer than this build
    // knows how to read — the store refuses to touch the file rather than
    // downgrading or destructively recreating it.
    UnsupportedSchema,

    // Any other failure (including invalid input reaching the store layer,
    // which should not happen if callers go through
    // FavoriteRepositoryIdentifier first).
    Unexpected
}
