using RepoPulse.AuthApi.Configuration;

namespace RepoPulse.AuthApi.Tests;

public class GitHubOAuthOptionsValidatorTests
{
    private static readonly GitHubOAuthOptionsValidator Validator = new();

    private static GitHubOAuthOptions ValidOptions() => new()
    {
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RedirectUri = "repopulse://oauth/callback",
        TokenEndpoint = "https://github.com/login/oauth/access_token"
    };

    [Fact]
    public void Validate_CorrectTestConfiguration_Succeeds()
    {
        var result = Validator.Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MissingClientId_Fails()
    {
        var options = ValidOptions();
        options.ClientId = string.Empty;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(GitHubOAuthOptions.ClientId)));
    }

    [Fact]
    public void Validate_MissingClientSecret_Fails()
    {
        var options = ValidOptions();
        options.ClientSecret = string.Empty;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(GitHubOAuthOptions.ClientSecret)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("relative/path")]
    public void Validate_InvalidRedirectUri_Fails(string invalidRedirectUri)
    {
        var options = ValidOptions();
        options.RedirectUri = invalidRedirectUri;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(GitHubOAuthOptions.RedirectUri)));
    }

    [Fact]
    public void Validate_CustomSchemeRedirectUri_IsAccepted()
    {
        // repopulse:// is a deliberate custom scheme, not http(s) — must not be
        // rejected just for using a non-web scheme.
        var options = ValidOptions();
        options.RedirectUri = "repopulse://oauth/callback";

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("http://github.com/login/oauth/access_token")]
    public void Validate_InvalidOrNonHttpsTokenEndpoint_Fails(string invalidTokenEndpoint)
    {
        var options = ValidOptions();
        options.TokenEndpoint = invalidTokenEndpoint;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(GitHubOAuthOptions.TokenEndpoint)));
    }
}
