using Microsoft.Extensions.Options;

namespace RepoPulse.AuthApi.Configuration;

// Wired up with ValidateOnStart() in Program.cs, so a missing/invalid
// ClientId, ClientSecret, RedirectUri, or TokenEndpoint makes the host fail
// to start rather than run with broken OAuth configuration. Never logs or
// includes the actual option values in failure messages.
public sealed class GitHubOAuthOptionsValidator : IValidateOptions<GitHubOAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubOAuthOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientId)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add($"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientSecret)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.RedirectUri) || !Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out _))
        {
            failures.Add($"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.RedirectUri)} must be a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(options.TokenEndpoint) ||
            !Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out var tokenEndpointUri) ||
            tokenEndpointUri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.TokenEndpoint)} must be a valid HTTPS URI.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
