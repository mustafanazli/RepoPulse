namespace RepoPulse.AuthApi.Configuration;

// Bound from the "GitHubOAuth" configuration section. ClientSecret must never
// appear in appsettings.json or any file under source control — see
// docs/backend-auth.md for how it is supplied (user-secrets in Development,
// the GitHubOAuth__ClientSecret environment variable in Production).
public sealed class GitHubOAuthOptions
{
    public const string SectionName = "GitHubOAuth";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
}
