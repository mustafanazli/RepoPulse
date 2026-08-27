using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

public class RepositoryListItemTests
{
    private static GitHubRepository MakeRepository(
        string? description = null,
        string? language = null,
        DateTimeOffset? updatedAt = null,
        bool isArchived = false,
        bool isFork = false) =>
        new(
            "owner",
            "Repo",
            "owner/Repo",
            description,
            "https://github.com/owner/Repo",
            10,
            2,
            3,
            language,
            "main",
            isArchived,
            isFork,
            updatedAt,
            null);

    [Fact]
    public void FromRepository_MapsFullNameAndStats()
    {
        var item = RepositoryListItem.FromRepository(MakeRepository());

        Assert.Equal("owner/Repo", item.FullName);
        // GitHub's open_issues_count counts issues AND pull requests
        // together — must read the same as RepositoryDetailPage's label.
        Assert.Equal("10 yıldız · 2 fork · 3 açık issue + PR", item.StatsText);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("A real description", true)]
    public void FromRepository_HasDescription_ReflectsWhitespaceHandling(string? description, bool expected)
    {
        var item = RepositoryListItem.FromRepository(MakeRepository(description: description));

        Assert.Equal(expected, item.HasDescription);
        Assert.Equal(description, item.Description);
    }

    [Fact]
    public void FromRepository_NullLanguage_UsesFallbackText()
    {
        var item = RepositoryListItem.FromRepository(MakeRepository(language: null));

        Assert.Equal("Ana dil belirtilmemiş", item.LanguageText);
    }

    [Fact]
    public void FromRepository_NonNullLanguage_IncludesLanguageName()
    {
        var item = RepositoryListItem.FromRepository(MakeRepository(language: "C#"));

        Assert.Equal("Ana dil: C#", item.LanguageText);
    }

    [Fact]
    public void FromRepository_NullUpdatedAt_UsesFallbackText()
    {
        var item = RepositoryListItem.FromRepository(MakeRepository(updatedAt: null));

        Assert.Equal("Son güncelleme bilgisi yok", item.UpdatedText);
    }

    [Fact]
    public void FromRepository_NonNullUpdatedAt_FormatsAsLocalDate()
    {
        var updatedAt = DateTimeOffset.Parse("2026-01-15T10:30:00Z");
        var item = RepositoryListItem.FromRepository(MakeRepository(updatedAt: updatedAt));

        Assert.Equal($"Son güncelleme: {updatedAt.ToLocalTime():dd.MM.yyyy}", item.UpdatedText);
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, "Arşivlenmiş")]
    [InlineData(false, true, "Fork")]
    [InlineData(true, true, "Arşivlenmiş · Fork")]
    public void FromRepository_BadgeText_ReflectsArchivedAndForkFlags(bool isArchived, bool isFork, string? expectedBadge)
    {
        var item = RepositoryListItem.FromRepository(MakeRepository(isArchived: isArchived, isFork: isFork));

        Assert.Equal(expectedBadge, item.BadgeText);
        Assert.Equal(expectedBadge is not null, item.HasBadge);
    }

    // RP-012: FromRepository's isFavorite parameter defaults to false so
    // every pre-RP-012 call site above (which never passes it) keeps its
    // original, correct behavior.
    [Fact]
    public void FromRepository_NoFavoriteArgument_DefaultsToNotFavorite()
    {
        var item = RepositoryListItem.FromRepository(MakeRepository());

        Assert.False(item.IsFavorite);
        Assert.Equal("Favorilere ekle", item.FavoriteToggleLabel);
    }

    [Theory]
    [InlineData(false, "Favorilere ekle")]
    [InlineData(true, "Favorilerden çıkar")]
    public void FromRepository_IsFavorite_MapsToggleLabel(bool isFavorite, string expectedLabel)
    {
        var item = RepositoryListItem.FromRepository(MakeRepository(), isFavorite);

        Assert.Equal(isFavorite, item.IsFavorite);
        Assert.Equal(expectedLabel, item.FavoriteToggleLabel);
    }

    // Selecting a list item must only ever be able to carry the repository
    // model onward — mirrors RepositoryNavigationQueryBuilderTests' proof
    // for the single-search flow (RP-007), now for the list-selection path.
    [Fact]
    public void Repository_RoundTripsThroughNavigationQueryBuilder_CarriesOnlyRepositoryObject()
    {
        var repository = MakeRepository();
        var item = RepositoryListItem.FromRepository(repository);

        var query = RepositoryNavigationQueryBuilder.Build(item.Repository);

        var entry = Assert.Single(query);
        Assert.Equal(AppRoutes.RepositoryQueryKey, entry.Key);
        Assert.Same(repository, entry.Value);
    }
}
