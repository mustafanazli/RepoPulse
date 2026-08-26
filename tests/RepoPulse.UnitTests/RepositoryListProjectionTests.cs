using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// Pure Core-layer coverage for RP-011's client-side search/sort. No
// GitHubApiClient, no HttpMessageHandler anywhere in this file — Apply
// cannot issue a network request by construction (it only ever sees an
// already-loaded IReadOnlyList<GitHubRepository>), which these tests confirm
// structurally (no extra requests possible) rather than by mocking a client
// that would never be called anyway.
public class RepositoryListProjectionTests
{
    private static GitHubRepository MakeRepository(
        string fullName,
        string? description = null,
        DateTimeOffset? updatedAt = null)
    {
        var parts = fullName.Split('/');
        return new GitHubRepository(
            parts[0],
            parts[1],
            fullName,
            description,
            $"https://github.com/{fullName}",
            0,
            0,
            0,
            null,
            "main",
            false,
            false,
            updatedAt,
            null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_NullOrWhitespaceQuery_ReturnsAllRepositories(string? query)
    {
        var repositories = new[] { MakeRepository("owner/A"), MakeRepository("owner/B") };

        var result = RepositoryListProjection.Apply(repositories, query, RepositorySortOrder.UpdatedDescending);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_FullNameMatch_ReturnsMatchingRepository()
    {
        var repositories = new[] { MakeRepository("mustafanazli/RepoPulse"), MakeRepository("octocat/Hello-World") };

        var result = RepositoryListProjection.Apply(repositories, "RepoPulse", RepositorySortOrder.UpdatedDescending);

        var item = Assert.Single(result);
        Assert.Equal("mustafanazli/RepoPulse", item.FullName);
    }

    [Fact]
    public void Apply_DescriptionMatch_ReturnsMatchingRepository()
    {
        var repositories = new[]
        {
            MakeRepository("owner/A", description: "A mobile health assistant"),
            MakeRepository("owner/B", description: "Unrelated project")
        };

        var result = RepositoryListProjection.Apply(repositories, "health", RepositorySortOrder.UpdatedDescending);

        var item = Assert.Single(result);
        Assert.Equal("owner/A", item.FullName);
    }

    [Fact]
    public void Apply_MatchIsCaseInsensitive()
    {
        var repositories = new[] { MakeRepository("mustafanazli/RepoPulse") };

        var result = RepositoryListProjection.Apply(repositories, "repopulse", RepositorySortOrder.UpdatedDescending);

        Assert.Single(result);
    }

    [Fact]
    public void Apply_TrimsLeadingAndTrailingWhitespaceFromQuery()
    {
        var repositories = new[] { MakeRepository("mustafanazli/RepoPulse") };

        var result = RepositoryListProjection.Apply(repositories, "  RepoPulse  ", RepositorySortOrder.UpdatedDescending);

        Assert.Single(result);
    }

    [Fact]
    public void Apply_NullDescription_DoesNotThrowAndIsExcludedWhenOnlyDescriptionWouldMatch()
    {
        var repositories = new[] { MakeRepository("owner/A", description: null) };

        var result = RepositoryListProjection.Apply(repositories, "anything", RepositorySortOrder.UpdatedDescending);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_NoMatches_ReturnsEmptyList()
    {
        var repositories = new[] { MakeRepository("owner/A"), MakeRepository("owner/B") };

        var result = RepositoryListProjection.Apply(repositories, "nonexistent-xyz", RepositorySortOrder.UpdatedDescending);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_UpdatedDescending_OrdersByMostRecentFirst()
    {
        var older = MakeRepository("owner/Older", updatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var newer = MakeRepository("owner/Newer", updatedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var repositories = new[] { older, newer };

        var result = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.UpdatedDescending);

        Assert.Equal(new[] { "owner/Newer", "owner/Older" }, result.Select(r => r.FullName));
    }

    [Fact]
    public void Apply_UpdatedDescending_NullUpdatedAtSortsLast()
    {
        var withDate = MakeRepository("owner/HasDate", updatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var withoutDate = MakeRepository("owner/NoDate", updatedAt: null);
        var repositories = new[] { withoutDate, withDate };

        var result = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.UpdatedDescending);

        Assert.Equal(new[] { "owner/HasDate", "owner/NoDate" }, result.Select(r => r.FullName));
    }

    [Fact]
    public void Apply_UpdatedDescending_EqualDatesUseFullNameTiebreak()
    {
        var sameDate = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var repositories = new[]
        {
            MakeRepository("owner/Zebra", updatedAt: sameDate),
            MakeRepository("owner/Apple", updatedAt: sameDate)
        };

        var result = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.UpdatedDescending);

        Assert.Equal(new[] { "owner/Apple", "owner/Zebra" }, result.Select(r => r.FullName));
    }

    [Fact]
    public void Apply_NameAscending_IsCaseInsensitive()
    {
        var repositories = new[]
        {
            MakeRepository("owner/banana"),
            MakeRepository("owner/Apple"),
            MakeRepository("owner/cherry")
        };

        var result = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.NameAscending);

        Assert.Equal(new[] { "owner/Apple", "owner/banana", "owner/cherry" }, result.Select(r => r.FullName));
    }

    [Fact]
    public void Apply_NameAscending_SameNameDifferentCase_UsesDeterministicOrdinalTiebreak()
    {
        var lower = MakeRepository("owner/repo");
        var upper = MakeRepository("owner/Repo");
        var repositories = new[] { lower, upper };

        var firstResult = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.NameAscending);
        var secondResult = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.NameAscending);

        // Whatever the tie-break resolves to, it must be the exact same
        // order every time — never dependent on input order or unstable.
        Assert.Equal(firstResult.Select(r => r.HtmlUrl), secondResult.Select(r => r.HtmlUrl));
        Assert.Equal("owner/Repo", firstResult[0].FullName);
        Assert.Equal("owner/repo", firstResult[1].FullName);
    }

    [Fact]
    public void Apply_FilterThenSort_AppliesBothTogether()
    {
        var repositories = new[]
        {
            MakeRepository("owner/health-checker", updatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            MakeRepository("owner/health-tracker", updatedAt: DateTimeOffset.Parse("2026-03-01T00:00:00Z")),
            MakeRepository("owner/unrelated", updatedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
        };

        var result = RepositoryListProjection.Apply(repositories, "health", RepositorySortOrder.UpdatedDescending);

        Assert.Equal(new[] { "owner/health-tracker", "owner/health-checker" }, result.Select(r => r.FullName));
    }

    [Fact]
    public void Apply_DoesNotMutateSourceList()
    {
        var repositories = new List<GitHubRepository>
        {
            MakeRepository("owner/Zebra", updatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            MakeRepository("owner/Apple", updatedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
        };
        var originalOrder = repositories.Select(r => r.FullName).ToArray();

        _ = RepositoryListProjection.Apply(repositories, "a", RepositorySortOrder.NameAscending);

        Assert.Equal(originalOrder, repositories.Select(r => r.FullName));
    }

    [Fact]
    public void Apply_ReturnsSameRepositoryInstances_CarriesNoExtraTokenOrSessionData()
    {
        var repository = MakeRepository("owner/A");
        var repositories = new[] { repository };

        var result = RepositoryListProjection.Apply(repositories, null, RepositorySortOrder.UpdatedDescending);

        // The projection reorders/filters existing GitHubRepository
        // instances — it never constructs a new wrapper type that could
        // carry along extra fields (e.g. a token or session id).
        Assert.Same(repository, result[0]);
    }

    [Fact]
    public void Apply_IsStateless_SameQueryAndSortApplyToWhicheverSourceListIsPassed()
    {
        var firstGenerationList = new[] { MakeRepository("owner/Alpha"), MakeRepository("owner/Beta") };
        var secondGenerationList = new[] { MakeRepository("owner/Gamma"), MakeRepository("owner/Delta") };

        var firstResult = RepositoryListProjection.Apply(firstGenerationList, null, RepositorySortOrder.NameAscending);
        var secondResult = RepositoryListProjection.Apply(secondGenerationList, null, RepositorySortOrder.NameAscending);

        // Apply carries no memory of a previous call — the exact same
        // search text/sort order, reused across a session-generation reload
        // (RP-010), simply re-derives from whatever list is passed this
        // time, never from a stale one.
        Assert.Equal(new[] { "owner/Alpha", "owner/Beta" }, firstResult.Select(r => r.FullName));
        Assert.Equal(new[] { "owner/Delta", "owner/Gamma" }, secondResult.Select(r => r.FullName));
    }
}
