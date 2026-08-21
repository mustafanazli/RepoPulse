using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RepoPulse.Core.Authentication;

// Thin wrapper around the two GitHub HTTP calls this app needs. Takes an
// HttpClient via constructor injection (registered in MauiProgram.cs) rather
// than constructing its own, so tests can substitute a fake HttpMessageHandler.
public sealed class GitHubOAuthClient
{
    private readonly HttpClient _httpClient;

    public GitHubOAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TokenExchangeResult> ExchangeCodeForTokenAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OAuthConstants.TokenEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = OAuthConstants.GitHubClientId,
            ["code"] = code,
            ["redirect_uri"] = OAuthConstants.RedirectUri,
            ["code_verifier"] = codeVerifier
        });

        string body;
        HttpStatusCode statusCode;
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return TokenExchangeResult.Failure(TokenExchangeFailureReason.Timeout, "Token isteği zaman aşımına uğradı.");
        }
        catch (HttpRequestException)
        {
            return TokenExchangeResult.Failure(TokenExchangeFailureReason.NetworkError, "Token endpoint'ine ulaşılamadı.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return TokenExchangeResult.Failure(TokenExchangeFailureReason.MalformedResponse, "Token endpoint'i okunamayan bir yanıt döndürdü.");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("access_token", out var tokenProp) &&
                tokenProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(tokenProp.GetString()))
            {
                var accessToken = tokenProp.GetString()!;
                var tokenType = root.TryGetProperty("token_type", out var tt) && tt.ValueKind == JsonValueKind.String
                    ? tt.GetString() ?? "bearer"
                    : "bearer";
                var scope = root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String
                    ? sc.GetString()
                    : null;

                return TokenExchangeResult.Ok(new TokenExchangeSuccess(accessToken, tokenType, scope));
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errorProp))
            {
                var errorCode = errorProp.ValueKind == JsonValueKind.String ? errorProp.GetString() : "unknown_error";
                return TokenExchangeResult.Failure(TokenExchangeFailureReason.OAuthError, $"GitHub yetkilendirmeyi reddetti ({errorCode}).");
            }

            if (statusCode != HttpStatusCode.OK)
            {
                return TokenExchangeResult.Failure(TokenExchangeFailureReason.NonSuccessStatusCode, $"Token endpoint'i beklenmeyen bir durum kodu döndürdü ({(int)statusCode}).");
            }

            return TokenExchangeResult.Failure(TokenExchangeFailureReason.MalformedResponse, "Token endpoint'i beklenen alanları içermiyor.");
        }
    }

    public async Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthConstants.UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("RepoPulse");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        string body;
        HttpStatusCode statusCode;
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return GitHubUserResult.Failure("Kullanıcı bilgisi isteği zaman aşımına uğradı.");
        }
        catch (HttpRequestException)
        {
            return GitHubUserResult.Failure("GitHub'a ulaşılamadı.");
        }

        if (statusCode != HttpStatusCode.OK)
        {
            return GitHubUserResult.Failure($"GitHub kullanıcı isteği beklenmeyen bir durum kodu döndürdü ({(int)statusCode}).");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var login = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("login", out var loginProp) && loginProp.ValueKind == JsonValueKind.String
                ? loginProp.GetString()
                : null;
            var avatarUrl = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("avatar_url", out var avatarProp) && avatarProp.ValueKind == JsonValueKind.String
                ? avatarProp.GetString()
                : null;

            if (string.IsNullOrEmpty(login))
            {
                return GitHubUserResult.Failure("GitHub kullanıcı yanıtı beklenen alanları içermiyor.");
            }

            return GitHubUserResult.Success(new GitHubUser(login, avatarUrl));
        }
        catch (JsonException)
        {
            return GitHubUserResult.Failure("GitHub kullanıcı yanıtı okunamadı.");
        }
    }
}
