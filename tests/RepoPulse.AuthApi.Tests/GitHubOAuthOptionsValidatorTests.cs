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
    [InlineData("repopulse://evil-host/callback")]           // wrong host
    [InlineData("repopulse://oauth/different-path")]          // wrong path
    [InlineData("repopulse://oauth/callback?extra=1")]        // unexpected query
    [InlineData("https://oauth/callback")]                    // wrong scheme
    public void Validate_RedirectUriNotExactMatch_Fails(string invalidRedirectUri)
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

    [Fact]
    public void Validate_RedirectUriDifferentCaseScheme_IsStillAccepted()
    {
        // Scheme/host casing differences are normalized before comparing —
        // this is not a security-relevant difference for a custom scheme.
        var options = ValidOptions();
        options.RedirectUri = "RepoPulse://OAUTH/callback";

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("http://github.com/login/oauth/access_token")]                 // wrong scheme
    [InlineData("https://evil.example.com/login/oauth/access_token")]          // wrong host
    [InlineData("https://github.com/login/oauth/different_endpoint")]          // wrong path
    [InlineData("https://github.com/login/oauth/access_token?foo=bar")]        // unexpected query
    public void Validate_TokenEndpointNotExactMatch_Fails(string invalidTokenEndpoint)
    {
        var options = ValidOptions();
        options.TokenEndpoint = invalidTokenEndpoint;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(GitHubOAuthOptions.TokenEndpoint)));
    }

    [Fact]
    public void Validate_FailureMessages_DoNotContainActualConfiguredValue()
    {
        var options = ValidOptions();
        options.RedirectUri = "repopulse://attacker.example.com/steal";
        options.TokenEndpoint = "https://attacker.example.com/steal";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.DoesNotContain(result.Failures!, f => f.Contains("attacker.example.com"));
    }
}
