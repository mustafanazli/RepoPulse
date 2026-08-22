namespace RepoPulse.AuthApi.Contracts;

// Only "code" and "codeVerifier" exist on this type. Unknown JSON fields
// (e.g. an attacker-supplied "clientId" or "redirectUri") are rejected by
// JsonSerializerOptions.UnmappedMemberHandling = Disallow (see Program.cs) —
// there is no property here they could bind to even if that were relaxed.
public sealed class GitHubTokenExchangeRequest
{
    public string? Code { get; set; }
    public string? CodeVerifier { get; set; }
}
