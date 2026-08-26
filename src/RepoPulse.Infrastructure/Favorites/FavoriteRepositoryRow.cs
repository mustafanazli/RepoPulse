using SQLite;

namespace RepoPulse.Infrastructure.Favorites;

// sqlite-net-pcl's attribute-mapped row for the FavoriteRepositories table.
// Internal — RepoPulse.Core/the rest of the app only ever sees the plain
// FavoriteRepository domain record; this type exists purely so sqlite-net-pcl
// has something to bind columns to.
[Table(SqliteFavoriteRepositoryStore.TableName)]
internal sealed class FavoriteRepositoryRow
{
    [PrimaryKey]
    [Column("NormalizedFullName")]
    public string NormalizedFullName { get; set; } = string.Empty;

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
