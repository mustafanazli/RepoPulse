using System.Text.Json.Serialization;

namespace RepoPulse.AuthApi.GitHub;

// Raw shape of GitHub's https://github.com/login/oauth/access_token response
// (success and error cases share one shape). Internal — never exposed
// directly to callers; GitHubTokenExchangeService maps it to the public,
// sanitized GitHubTokenExchangeResponse contract.
internal sealed class GitHubUpstreamTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("refresh_token_expires_in")]
    public long? RefreshTokenExpiresIn { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}
