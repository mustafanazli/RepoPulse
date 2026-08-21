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

    public const string AuthorizeEndpoint = "https://github.com/login/oauth/authorize";
    public const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    public const string UserEndpoint = "https://api.github.com/user";
}
