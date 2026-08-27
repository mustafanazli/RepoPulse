using SQLite;

namespace RepoPulse.Infrastructure.Favorites;

// sqlite-net-pcl's attribute-mapped row for the FavoriteRepositories table.
// Internal — RepoPulse.Core/the rest of the app only ever sees the plain
// FavoriteRepository domain record; this type exists purely so sqlite-net-pcl
// has something to bind columns to.
//
// The real PRIMARY KEY — the composite (AccountLoginNormalized,
// NormalizedFullName) that fixes the cross-account favorite leak — is
// declared in SqliteFavoriteRepositoryStore's raw CREATE TABLE SQL, not via
// a [PrimaryKey] attribute here: sqlite-net-pcl's attribute only supports a
// single-column key, and this table is never created via CreateTableAsync<T>
// anyway. [NotNull] on both identity columns is still enforced by SQLite's
// real schema.
[Table(SqliteFavoriteRepositoryStore.TableName)]
internal sealed class FavoriteRepositoryRow
{
    [NotNull]
    [Column("AccountLoginNormalized")]
    public string AccountLoginNormalized { get; set; } = string.Empty;

    [NotNull]
    [Column("NormalizedFullName")]
    public string NormalizedFullName { get; set; } = string.Empty;

    // Original-cased GitHub login, kept alongside the normalized key purely
    // for symmetry with Owner/Name below — never used for lookups/identity.
    [NotNull]
    [Column("AccountLogin")]
    public string AccountLogin { get; set; } = string.Empty;

    [NotNull]
    [Column("Owner")]
    public string Owner { get; set; } = string.Empty;

    [NotNull]
    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    // Unix seconds (UTC) — never a locale-formatted string, so the value is
    // unambiguous and sortable regardless of the device's culture/locale.
    [NotNull]
    [Column("AddedAtUtc")]
    public long AddedAtUtc { get; set; }
}
