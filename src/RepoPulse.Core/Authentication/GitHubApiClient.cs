using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RepoPulse.Core.Repositories;

namespace RepoPulse.Core.Authentication;

public interface IGitHubApiClient
{
    Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken);

    Task<GitHubRepositoryResult> GetRepositoryAsync(string accessToken, string owner, string name, CancellationToken cancellationToken);
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
        // See the matching comment in RepoPulseAuthApiClient.ExchangeAsync —
        // Xamarin.Android's HTTP handler can surface a raw socket failure as
        // System.Net.WebException instead of HttpRequestException.
        catch (WebException)
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

    // Single-repository lookup (RP-006): GET /repos/{owner}/{name}. Owner and
    // name are ALWAYS the two segments produced by RepositoryIdentifierParser
    // — never raw, unvalidated user input — and are additionally
    // percent-encoded here before being placed in the URL, so neither can
    // inject an extra path segment or query string even if that invariant
    // were ever violated by a future caller.
    public async Task<GitHubRepositoryResult> GetRepositoryAsync(string accessToken, string owner, string name, CancellationToken cancellationToken)
    {
        var requestUri = $"{OAuthConstants.RepositoryEndpointBase}/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
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
            return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.NetworkError);
        }
        catch (HttpRequestException)
        {
            return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.NetworkError);
        }
        // See the matching comment in RepoPulseAuthApiClient.ExchangeAsync —
        // Xamarin.Android's HTTP handler can surface a raw socket failure as
        // System.Net.WebException instead of HttpRequestException.
        catch (WebException)
        {
            return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.NetworkError);
        }

        // Status code alone decides the outcome — the response body (which
        // may carry GitHub's own error message) is never surfaced to the
        // caller/UI/logs for any non-success status.
        switch (statusCode)
        {
            case HttpStatusCode.OK:
                return ParseRepository(body);
            case HttpStatusCode.NotFound:
                return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.NotFound);
            case HttpStatusCode.Unauthorized:
                return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unauthorized);
            case HttpStatusCode.Forbidden:
            case (HttpStatusCode)429:
                return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.RateLimited);
            default:
                return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unexpected);
        }
    }

    private static GitHubRepositoryResult ParseRepository(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unexpected);
            }

            string? GetString(string propertyName) =>
                root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            int GetInt(string propertyName) =>
                root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                    ? number
                    : 0;

            bool GetBool(string propertyName) =>
                root.TryGetProperty(propertyName, out var value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) &&
                value.GetBoolean();

            DateTimeOffset? GetDate(string propertyName) =>
                root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), out var parsed)
                    ? parsed
                    : null;

            var owner = root.TryGetProperty("owner", out var ownerElement) && ownerElement.ValueKind == JsonValueKind.Object
                ? (ownerElement.TryGetProperty("login", out var loginElement) && loginElement.ValueKind == JsonValueKind.String
                    ? loginElement.GetString()
                    : null)
                : null;

            var name = GetString("name");
            var fullName = GetString("full_name");
            var htmlUrl = GetString("html_url");
            var defaultBranch = GetString("default_branch");

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullName) ||
                string.IsNullOrEmpty(htmlUrl) || string.IsNullOrEmpty(defaultBranch))
            {
                return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unexpected);
            }

            var repository = new GitHubRepository(
                owner,
                name,
                fullName,
                GetString("description"),
                htmlUrl,
                GetInt("stargazers_count"),
                GetInt("forks_count"),
                GetInt("open_issues_count"),
                GetString("language"),
                defaultBranch,
                GetBool("archived"),
                GetBool("fork"),
                GetDate("updated_at"),
                GetDate("pushed_at"));

            return GitHubRepositoryResult.Success(repository);
        }
        catch (JsonException)
        {
            return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unexpected);
        }
    }
}
