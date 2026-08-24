using RepoPulse.Core.Navigation;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

public class RepositoryNavigationQueryBuilderTests
{
    private static GitHubRepository CreateRepository() => new(
        "octocat",
        "Hello-World",
        "octocat/Hello-World",
        "A sample repository",
        "https://github.com/octocat/Hello-World",
        Stars: 42,
        Forks: 7,
        OpenIssuesAndPullRequests: 3,
        PrimaryLanguage: "C#",
        DefaultBranch: "main",
        IsArchived: false,
        IsFork: false,
        UpdatedAt: DateTimeOffset.UtcNow,
        PushedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Build_ContainsExactlyOneEntry_TheRepositoryItself()
    {
        var repository = CreateRepository();

        var query = RepositoryNavigationQueryBuilder.Build(repository);

        Assert.Single(query);
        Assert.Same(repository, query[AppRoutes.RepositoryQueryKey]);
    }

    [Fact]
    public void Build_QueryValue_HasNoTokenShapedField()
    {
        var repository = CreateRepository();

        var query = RepositoryNavigationQueryBuilder.Build(repository);
        var value = Assert.IsType<GitHubRepository>(query[AppRoutes.RepositoryQueryKey]);

        var propertyNames = value.GetType().GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }
}
