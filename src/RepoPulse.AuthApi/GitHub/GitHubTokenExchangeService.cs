using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RepoPulse.AuthApi.Configuration;
using RepoPulse.AuthApi.Contracts;

namespace RepoPulse.AuthApi.GitHub;

public interface IGitHubTokenExchangeService
{
    Task<GitHubTokenExchangeOutcome> ExchangeAsync(string code, string codeVerifier, CancellationToken cancellationToken);
}

// Exactly one outbound call per invocation — no automatic retry, since the
// authorization code and code_verifier are single-use and a retry with the
// same code would itself be rejected by GitHub as already-consumed.
public sealed class GitHubTokenExchangeService : IGitHubTokenExchangeService
{
    private readonly HttpClient httpClient;
    private readonly GitHubOAuthOptions options;

    public GitHubTokenExchangeService(HttpClient httpClient, IOptions<GitHubOAuthOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<GitHubTokenExchangeOutcome> ExchangeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        // TokenEndpoint is intentionally never taken from the request — it
        // always comes from trusted server-side configuration.
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("RepoPulse-AuthApi");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = options.RedirectUri,
            ["code_verifier"] = codeVerifier
        });

        string body;
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return GitHubTokenExchangeOutcome.Failure(GitHubTokenExchangeFailureKind.UpstreamTimeout);
        }
        catch (HttpRequestException)
        {
            return GitHubTokenExchangeOutcome.Failure(GitHubTokenExchangeFailureKind.UpstreamError);
        }

        GitHubUpstreamTokenResponse? upstream;
        try
        {
            upstream = JsonSerializer.Deserialize<GitHubUpstreamTokenResponse>(body);
        }
        catch (JsonException)
        {
            return GitHubTokenExchangeOutcome.Failure(GitHubTokenExchangeFailureKind.UpstreamError);
        }

        if (upstream is null)
        {
            return GitHubTokenExchangeOutcome.Failure(GitHubTokenExchangeFailureKind.UpstreamError);
        }

        if (!string.IsNullOrEmpty(upstream.AccessToken))
        {
            return GitHubTokenExchangeOutcome.Ok(new GitHubTokenExchangeResponse
            {
                AccessToken = upstream.AccessToken,
                TokenType = string.IsNullOrEmpty(upstream.TokenType) ? "bearer" : upstream.TokenType,
                Scope = upstream.Scope,
                ExpiresIn = upstream.ExpiresIn,
                RefreshToken = upstream.RefreshToken,
                RefreshTokenExpiresIn = upstream.RefreshTokenExpiresIn
            });
        }

        // GitHub reports OAuth-level rejections (bad code, denied, etc.) as a
        // JSON body containing "error" — the raw error/error_description is
        // deliberately not propagated to the caller (see Program.cs mapping).
        if (!string.IsNullOrEmpty(upstream.Error))
        {
            return GitHubTokenExchangeOutcome.Failure(GitHubTokenExchangeFailureKind.OAuthRejected);
        }

        return GitHubTokenExchangeOutcome.Failure(GitHubTokenExchangeFailureKind.UpstreamError);
    }
}
