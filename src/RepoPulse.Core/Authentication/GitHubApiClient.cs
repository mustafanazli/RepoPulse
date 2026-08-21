using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RepoPulse.Core.Authentication;

public interface IGitHubApiClient
{
    Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken);
}

// Single responsibility: GET /user. Does not know about OAuth, PKCE, or the
// token exchange — it only ever receives an already-obtained access token.
public sealed class GitHubApiClient : IGitHubApiClient
{
    private readonly HttpClient httpClient;

    public GitHubApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
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
            using var response = await httpClient.SendAsync(request, cancellationToken);
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
