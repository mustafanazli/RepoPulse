namespace RepoPulse.Core.Authentication;

// No 'scope' parameter is included: the MVP only reads public repository data,
// and an unauthenticated-scope token already grants read access to public
// resources on behalf of the user. Do not add repo/other scopes here without
// updating the plan's MVP-kapsam rationale.
public static class GitHubAuthorizationUrlBuilder
{
    public static string Build(string clientId, string redirectUri, string state, string codeChallenge) =>
        OAuthConstants.AuthorizeEndpoint +
        "?client_id=" + Uri.EscapeDataString(clientId) +
        "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
        "&state=" + Uri.EscapeDataString(state) +
        "&code_challenge=" + Uri.EscapeDataString(codeChallenge) +
        "&code_challenge_method=S256";
}
