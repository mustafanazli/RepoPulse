using Microsoft.Extensions.Options;

namespace RepoPulse.AuthApi.Configuration;

// Wired up with ValidateOnStart() in Program.cs, so a missing/invalid
// ClientId, ClientSecret, RedirectUri, or TokenEndpoint makes the host fail
// to start rather than run with broken OAuth configuration. Never logs or
// includes the actual configured values in failure messages — only the
// fixed, non-secret expected values (which are not sensitive) are named.
public sealed class GitHubOAuthOptionsValidator : IValidateOptions<GitHubOAuthOptions>
{
    // These are fixed by design: the GitHub OAuth App is registered with
    // exactly this callback, and only GitHub's own token endpoint is ever
    // called. Configuration must match exactly, not merely "be a URI".
    internal const string ExpectedRedirectUri = "repopulse://oauth/callback";
    internal const string ExpectedTokenEndpoint = "https://github.com/login/oauth/access_token";

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

        if (!MatchesExpectedUri(options.RedirectUri, ExpectedRedirectUri))
        {
            failures.Add($"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.RedirectUri)} must be exactly '{ExpectedRedirectUri}'.");
        }

        if (!MatchesExpectedUri(options.TokenEndpoint, ExpectedTokenEndpoint))
        {
            failures.Add($"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.TokenEndpoint)} must be exactly '{ExpectedTokenEndpoint}'.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    // Parses both sides as URIs and compares scheme/host/path/query rather
    // than raw strings, so e.g. casing differences in the scheme don't
    // falsely fail — but a different host, path, or query is always rejected,
    // never silently "close enough" accepted.
    private static bool MatchesExpectedUri(string? actualValue, string expectedValue)
    {
        if (string.IsNullOrWhiteSpace(actualValue))
        {
            return false;
        }

        if (!Uri.TryCreate(actualValue, UriKind.Absolute, out var actual) ||
            !Uri.TryCreate(expectedValue, UriKind.Absolute, out var expected))
        {
            return false;
        }

        return string.Equals(actual.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
            && actual.AbsolutePath == expected.AbsolutePath
            && actual.Query == expected.Query;
    }
}
