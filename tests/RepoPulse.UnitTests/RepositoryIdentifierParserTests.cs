using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

public class RepositoryIdentifierParserTests
{
    [Theory]
    [InlineData("mustafanazli/RepoPulse", "mustafanazli", "RepoPulse")]
    [InlineData("octocat/Hello-World", "octocat", "Hello-World")]
    [InlineData("  mustafanazli/RepoPulse  ", "mustafanazli", "RepoPulse")]
    [InlineData("dotnet/runtime.linker", "dotnet", "runtime.linker")]
    public void Parse_OwnerSlashRepository_Succeeds(string input, string expectedOwner, string expectedName)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedOwner, result.Value!.Owner);
        Assert.Equal(expectedName, result.Value.Name);
    }

    [Theory]
    [InlineData("https://github.com/mustafanazli/RepoPulse", "mustafanazli", "RepoPulse")]
    [InlineData("http://github.com/mustafanazli/RepoPulse", "mustafanazli", "RepoPulse")]
    [InlineData("https://www.github.com/mustafanazli/RepoPulse", "mustafanazli", "RepoPulse")]
    [InlineData("https://github.com/mustafanazli/RepoPulse.git", "mustafanazli", "RepoPulse")]
    [InlineData("https://github.com/mustafanazli/RepoPulse/", "mustafanazli", "RepoPulse")]
    public void Parse_GitHubUrl_Succeeds(string input, string expectedOwner, string expectedName)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedOwner, result.Value!.Owner);
        Assert.Equal(expectedName, result.Value.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrWhitespaceInput_Fails(string? input)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.NotNull(result.SafeErrorMessage);
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://evil.com/github.com/owner/repo")]
    [InlineData("https://notgithub.com/owner/repo")]
    [InlineData("ftp://github.com/owner/repo")]
    public void Parse_NonGitHubHostOrScheme_Fails(string input)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/issues")]
    [InlineData("https://github.com/owner/repo/pulls/1")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com")]
    public void Parse_UrlWithWrongSegmentCount_Fails(string input)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo?tab=readme")]
    [InlineData("https://github.com/owner/repo#readme")]
    [InlineData("https://github.com/owner/repo?token=abc123")]
    public void Parse_UrlWithQueryOrFragment_Fails(string input)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("/repo")]
    [InlineData("owner/")]
    [InlineData("owner//repo")]
    public void Parse_PlainFormWithWrongSegmentCount_Fails(string input)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("owner/repo name")]
    [InlineData("owner name/repo")]
    [InlineData("owner/repo?x=1")]
    [InlineData("owner/../etc")]
    [InlineData("../owner/repo")]
    [InlineData("owner/<script>")]
    [InlineData("owner/repo;drop")]
    public void Parse_DangerousOrInvalidCharacters_Fails(string input)
    {
        var result = RepositoryIdentifierParser.Parse(input);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_OwnerTooLong_Fails()
    {
        var owner = new string('a', 40);

        var result = RepositoryIdentifierParser.Parse($"{owner}/repo");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_NameTooLong_Fails()
    {
        var name = new string('a', 101);

        var result = RepositoryIdentifierParser.Parse($"owner/{name}");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_NameExactlyAtMaxLength_Succeeds()
    {
        var name = new string('a', 100);

        var result = RepositoryIdentifierParser.Parse($"owner/{name}");

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value!.Name);
    }

    [Fact]
    public void Parse_SingleDotOrDoubleDotSegment_Fails()
    {
        Assert.False(RepositoryIdentifierParser.Parse("./repo").IsSuccess);
        Assert.False(RepositoryIdentifierParser.Parse("owner/.").IsSuccess);
        Assert.False(RepositoryIdentifierParser.Parse("owner/..").IsSuccess);
    }
}
