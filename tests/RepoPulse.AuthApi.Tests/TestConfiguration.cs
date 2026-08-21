namespace RepoPulse.AuthApi.Tests;

internal static class TestConfiguration
{
    public static Dictionary<string, string?> Valid() => new()
    {
        ["GitHubOAuth:ClientId"] = "test-client-id",
        ["GitHubOAuth:ClientSecret"] = "test-client-secret",
        ["GitHubOAuth:RedirectUri"] = "repopulse://oauth/callback",
        ["GitHubOAuth:TokenEndpoint"] = "https://github.com/login/oauth/access_token"
    };

    // Exactly 43 chars — the minimum valid RFC 7636 code_verifier length.
    public static readonly string ValidVerifier = new('A', 43);
}
