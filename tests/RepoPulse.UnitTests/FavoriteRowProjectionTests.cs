using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

public class FavoriteRowProjectionTests
{
    private static GitHubRepository MakeRepository(string owner, string name, string? description = null) =>
        new(owner, name, $"{owner}/{name}", description, $"https://github.com/{owner}/{name}", 0, 0, 0, null, "main", false, false, null, null);

    private static FavoriteRepository MakeFavorite(string owner, string name, DateTimeOffset addedAtUtc)
    {
        FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
        return new FavoriteRepository(identity.Owner, identity.Name, identity.NormalizedFullName, addedAtUtc);
    }

    [Fact]
    public void Apply_FavoritePresentInLiveList_ProducesFullRepositoryListItemMarkedFavorite()
    {
        var live = new[] { MakeRepository("owner", "A", description: "desc") };
        var favorites = new[] { MakeFavorite("owner", "A", DateTimeOffset.UtcNow) };

        var rows = FavoriteRowProjection.Apply(live, favorites, null);

        var item = Assert.IsType<RepositoryListItem>(Assert.Single(rows));
        Assert.True(item.IsFavorite);
        Assert.Equal("owner/A", item.FullName);
        Assert.Equal("desc", item.Description);
    }

    [Fact]
    public void Apply_FavoriteNotInLiveList_ProducesIdentityOnlyRow()
    {
        var favorites = new[] { MakeFavorite("owner", "Offline", DateTimeOffset.UtcNow) };

        var rows = FavoriteRowProjection.Apply(Array.Empty<GitHubRepository>(), favorites, null);

        var row = Assert.IsType<FavoriteIdentityRow>(Assert.Single(rows));
        Assert.Equal("owner", row.Owner);
        Assert.Equal("Offline", row.Name);
        Assert.Equal("owner/Offline", row.FullName);
    }

    // The identity-only row must never expose anything beyond
    // owner/name/FullName/AddedAtUtc/AddedAtText — no field it could use to
    // fake stars/description/language, and nothing token- or
    // session-shaped.
    [Fact]
    public void Apply_IdentityOnlyRow_CarriesNoApiSnapshotOrTokenShapedField()
    {
        var favorites = new[] { MakeFavorite("owner", "Offline", DateTimeOffset.UtcNow) };

        var rows = FavoriteRowProjection.Apply(Array.Empty<GitHubRepository>(), favorites, null);
        var row = Assert.IsType<FavoriteIdentityRow>(Assert.Single(rows));

        var properties = typeof(FavoriteIdentityRow).GetProperties().Select(p => p.Name).ToArray();
        var allowed = new[] { "NormalizedFullName", "Owner", "Name", "FullName", "AddedAtUtc", "AddedAtText", "EqualityContract" };
        Assert.All(properties, name => Assert.Contains(name, allowed));
        Assert.Equal(FavoriteIdentityRow.OfflineNoticeText, "Ayrıntılar için bağlantı gerekli.");
        _ = row;
    }

    [Fact]
    public void Apply_MixOfLiveAndOfflineFavorites_OrdersNewestFirst()
    {
        var live = new[] { MakeRepository("owner", "Live") };
        var older = MakeFavorite("owner", "Live", DateTimeOffset.UtcNow.AddDays(-2));
        var newer = MakeFavorite("owner", "Offline", DateTimeOffset.UtcNow);

        var rows = FavoriteRowProjection.Apply(live, new[] { older, newer }, null);

        Assert.Equal(2, rows.Count);
        Assert.IsType<FavoriteIdentityRow>(rows[0]);
        Assert.IsType<RepositoryListItem>(rows[1]);
    }

    [Fact]
    public void Apply_EqualAddedAtUtc_BreaksTieByNormalizedFullNameForDeterminism()
    {
        var same = DateTimeOffset.UtcNow;
        var favorites = new[] { MakeFavorite("owner", "Zeta", same), MakeFavorite("owner", "Alpha", same) };

        var rows = FavoriteRowProjection.Apply(Array.Empty<GitHubRepository>(), favorites, null);

        var names = rows.Cast<FavoriteIdentityRow>().Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "Alpha", "Zeta" }, names);
    }

    [Fact]
    public void Apply_SearchTextMatchesFullNameCaseInsensitively()
    {
        var favorites = new[] { MakeFavorite("owner", "Matching", DateTimeOffset.UtcNow), MakeFavorite("owner", "Other", DateTimeOffset.UtcNow) };

        var rows = FavoriteRowProjection.Apply(Array.Empty<GitHubRepository>(), favorites, "MATCH");

        var row = Assert.IsType<FavoriteIdentityRow>(Assert.Single(rows));
        Assert.Equal("Matching", row.Name);
    }

    [Fact]
    public void Apply_SearchTextMatchesNothing_ReturnsEmpty()
    {
        var favorites = new[] { MakeFavorite("owner", "Alpha", DateTimeOffset.UtcNow) };

        var rows = FavoriteRowProjection.Apply(Array.Empty<GitHubRepository>(), favorites, "nonexistent");

        Assert.Empty(rows);
    }

    [Fact]
    public void Apply_NoFavorites_ReturnsEmptyRegardlessOfLiveList()
    {
        var live = new[] { MakeRepository("owner", "A") };

        var rows = FavoriteRowProjection.Apply(live, Array.Empty<FavoriteRepository>(), null);

        Assert.Empty(rows);
    }

    [Fact]
    public void Apply_NeverMutatesInputCollections()
    {
        var live = new List<GitHubRepository> { MakeRepository("owner", "A") };
        var favorites = new List<FavoriteRepository> { MakeFavorite("owner", "A", DateTimeOffset.UtcNow) };

        FavoriteRowProjection.Apply(live, favorites, null);

        Assert.Single(live);
        Assert.Single(favorites);
    }
}
