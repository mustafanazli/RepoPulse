namespace RepoPulse.Core.Authentication;

public static class OAuthConstants
{
    public const string CallbackScheme = "repopulse";
    public const string CallbackHost = "oauth";
    public const string CallbackPath = "/callback";
    public const string RedirectUri = CallbackScheme + "://" + CallbackHost + CallbackPath;

    // GitHub OAuth App client IDs are public identifiers, not secrets — this app
    // is a PKCE public client and has no client secret (see RP-003 report).
    public const string GitHubClientId = "Ov23likVt8K7YO1aqnfo";

    // No token endpoint here on purpose: the mobile app never calls GitHub's
    // token endpoint directly (see ADR-003 / RP-005) — RepoPulseAuthApiClient
    // talks to our own backend instead, which holds the client_secret.
    public const string AuthorizeEndpoint = "https://github.com/login/oauth/authorize";
    public const string UserEndpoint = "https://api.github.com/user";

    // Base address for repository lookups (RP-006) — GitHubApiClient appends
    // /{owner}/{repository}, both percent-encoded. See
    // RepoPulse.Core.Repositories.RepositoryIdentifierParser for the only
    // place user input is turned into these two path segments.
    public const string RepositoryEndpointBase = "https://api.github.com/repos";

    // Authenticated user's repository list (RP-009). Host/path are also used
    // as the trust anchor GitHubApiClient checks a paginated Link header's
    // "next" URL against before ever following it.
    public const string RepositoryListHost = "api.github.com";
    public const string RepositoryListPath = "/user/repos";
    public const string RepositoryListEndpoint = "https://" + RepositoryListHost + RepositoryListPath;

    // GitHub's single GraphQL endpoint (RP-020) — used only by
    // GetOldestOpenIssueAsync's repository.issues query. Every other
    // GitHubApiClient method talks to a REST endpoint above; this is the
    // only POST/GraphQL call in the client.
    public const string GraphQlEndpoint = "https://api.github.com/graphql";
}
