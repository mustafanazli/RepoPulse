using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RepoPulse.Core.Repositories;

namespace RepoPulse.Core.Authentication;

public interface IGitHubApiClient
{
    Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken);

    Task<GitHubRepositoryResult> GetRepositoryAsync(string accessToken, string owner, string name, CancellationToken cancellationToken);

    Task<GitHubRepositoryListResult> GetUserRepositoriesAsync(string accessToken, CancellationToken cancellationToken);
}

// Single responsibility: GET /user. Does not know about OAuth, PKCE, or the
// token exchange — it only ever receives an already-obtained access token.
public sealed class GitHubApiClient : IGitHubApiClient
{
    // GetUserRepositoriesAsync (RP-009) pagination bounds: 10 pages at 100
    // repositories per page caps a single call at 1000 repositories, win or
    // lose — a deliberate, small, fixed ceiling rather than following GitHub
    // pagination indefinitely.
    private const int RepositoryListPageSize = 100;
    private const int RepositoryListMaxPages = 10;

    private readonly HttpClient httpClient;

    public GitHubApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthConstants.UserEndpoint);
        ApplyStandardHeaders(request, accessToken);

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
        ApplyStandardHeaders(request, accessToken);

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

    // Authenticated user's repository list (RP-009): GET /user/repos, most
    // recently updated first. Every page is fetched from a URI this method
    // itself builds from OAuthConstants.RepositoryListEndpoint — a page
    // number extracted from GitHub's own Link header is the only thing ever
    // taken from that header (see TryExtractValidatedNextPage), never a URL
    // followed as-is.
    public async Task<GitHubRepositoryListResult> GetUserRepositoriesAsync(string accessToken, CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepository>();
        var seenFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedPages = new HashSet<int> { 1 };
        var isTruncated = false;

        var requestUri = BuildRepositoryListUri(page: null);

        for (var pageNumber = 1; pageNumber <= RepositoryListMaxPages; pageNumber++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            ApplyStandardHeaders(request, accessToken);

            string body;
            HttpStatusCode statusCode;
            string? linkHeaderValue;
            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                statusCode = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                linkHeaderValue = response.Headers.TryGetValues("Link", out var linkValues)
                    ? linkValues.FirstOrDefault()
                    : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.NetworkError);
            }
            catch (HttpRequestException)
            {
                return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.NetworkError);
            }
            // See the matching comment in RepoPulseAuthApiClient.ExchangeAsync
            // — Xamarin.Android's HTTP handler can surface a raw socket
            // failure as System.Net.WebException instead of
            // HttpRequestException.
            catch (WebException)
            {
                return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.NetworkError);
            }

            // Status code alone decides the outcome for every page — a
            // failure on page 2+ is reported the same as a failure on page 1,
            // never silently downgraded to a partial success.
            switch (statusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.Unauthorized:
                    return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.Unauthorized);
                case HttpStatusCode.Forbidden:
                case (HttpStatusCode)429:
                    return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.RateLimited);
                default:
                    return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.Unexpected);
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.Unexpected);
                }

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (!TryParseRepositoryElement(element, out var repository))
                    {
                        // A record missing a required field (owner/name/
                        // full_name/html_url/default_branch) is skipped
                        // rather than failing the whole page — but the
                        // caller must never be told the list is complete
                        // when it is not: dropping a record always marks
                        // the result truncated.
                        isTruncated = true;
                        continue;
                    }

                    if (seenFullNames.Add(repository.FullName))
                    {
                        repositories.Add(repository);
                    }
                }
            }
            catch (JsonException)
            {
                return GitHubRepositoryListResult.Failure(GitHubRepositoryFailureKind.Unexpected);
            }

            var (hasNextRel, nextUrl) = TryFindNextLinkUrl(linkHeaderValue);

            if (!hasNextRel)
            {
                // No rel="next" entry at all — GitHub itself says there is no
                // more data, so this result is complete, not truncated.
                break;
            }

            var nextPageNumber = ValidateAndExtractPageNumber(nextUrl!, expectedPage: pageNumber + 1);

            if (nextPageNumber is null)
            {
                // GitHub indicated more data exists, but the "next" URL
                // failed a trust check (wrong scheme/host/port/userinfo/
                // fragment/path/query, or was simply unparsable) — stop
                // rather than follow it, but do not claim the result is
                // complete.
                isTruncated = true;
                break;
            }

            if (pageNumber == RepositoryListMaxPages || !visitedPages.Add(nextPageNumber.Value))
            {
                // Either the page ceiling was reached while GitHub still
                // reports more data, or the "next" link points at a page
                // already fetched (a looping Link header) — stop rather than
                // trusting it, and report the result as truncated either way.
                isTruncated = true;
                break;
            }

            requestUri = BuildRepositoryListUri(nextPageNumber);
        }

        return GitHubRepositoryListResult.Success(repositories, isTruncated);
    }

    private static void ApplyStandardHeaders(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("RepoPulse");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    private static string BuildRepositoryListUri(int? page) =>
        page is null
            ? $"{OAuthConstants.RepositoryListEndpoint}?sort=updated&direction=desc&per_page={RepositoryListPageSize}"
            : $"{OAuthConstants.RepositoryListEndpoint}?sort=updated&direction=desc&per_page={RepositoryListPageSize}&page={page.Value}";

    // Finds the rel="next" entry in an RFC 8288 Link header, returning its
    // raw URL (not yet trusted/validated) and whether one was present at
    // all. Kept separate from validation so "no next link" (definitely no
    // more pages) can be told apart from "a next link was present but did
    // not pass ValidateAndExtractPageNumber" (unknown whether more pages
    // exist — must not be reported as a complete result).
    private static (bool Found, string? Url) TryFindNextLinkUrl(string? linkHeaderValue)
    {
        if (string.IsNullOrEmpty(linkHeaderValue))
        {
            return (false, null);
        }

        foreach (var rawSegment in linkHeaderValue.Split(','))
        {
            var segment = rawSegment.Trim();
            var linkStart = segment.IndexOf('<');
            var linkEnd = segment.IndexOf('>');
            if (linkStart != 0 || linkEnd <= linkStart)
            {
                continue;
            }

            var attributes = segment[(linkEnd + 1)..];
            if (!attributes.Contains("rel=\"next\"", StringComparison.Ordinal))
            {
                continue;
            }

            return (true, segment[(linkStart + 1)..linkEnd]);
        }

        return (false, null);
    }

    // expectedPage is always the current page number + 1 — GitHub's own
    // pages are consumed strictly in order, one at a time; a "next" link
    // that names any other page (a skip, a repeat, or a jump backwards) is
    // rejected exactly like a link that fails the host/path/scheme checks.
    private static int? ValidateAndExtractPageNumber(string url, int expectedPage)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, OAuthConstants.RepositoryListHost, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != OAuthConstants.RepositoryListPath)
        {
            return null;
        }

        var queryString = uri.Query.StartsWith('?') ? uri.Query[1..] : uri.Query;
        if (queryString.Length == 0)
        {
            return null;
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        int? page = null;

        // Deliberately NOT StringSplitOptions.RemoveEmptyEntries — a stray
        // "&&" or trailing "&" must be rejected, not silently ignored.
        foreach (var pair in queryString.Split('&'))
        {
            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex < 0)
            {
                return null;
            }

            var key = Uri.UnescapeDataString(pair[..equalsIndex]);
            var value = Uri.UnescapeDataString(pair[(equalsIndex + 1)..]);

            if (key.Length == 0 || value.Length == 0)
            {
                return null;
            }

            if (!seenKeys.Add(key))
            {
                // The same query key must never appear twice.
                return null;
            }

            switch (key)
            {
                case "page":
                    if (!int.TryParse(value, out var parsedPage) || parsedPage != expectedPage)
                    {
                        return null;
                    }
                    page = parsedPage;
                    break;
                case "per_page":
                    if (value != "100")
                    {
                        return null;
                    }
                    break;
                case "sort":
                    if (value != "updated")
                    {
                        return null;
                    }
                    break;
                case "direction":
                    if (value != "desc")
                    {
                        return null;
                    }
                    break;
                default:
                    // Any parameter outside the expected set makes this
                    // "next" link untrusted.
                    return null;
            }
        }

        return page;
    }

    private static GitHubRepositoryResult ParseRepository(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return TryParseRepositoryElement(document.RootElement, out var repository)
                ? GitHubRepositoryResult.Success(repository)
                : GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unexpected);
        }
        catch (JsonException)
        {
            return GitHubRepositoryResult.Failure(GitHubRepositoryFailureKind.Unexpected);
        }
    }

    // Shared by the single-repository lookup (RP-006) and the repository
    // list (RP-009) — one JSON object shape, whether it arrived as a
    // standalone response body or as one element of a /user/repos array.
    private static bool TryParseRepositoryElement(JsonElement root, out GitHubRepository repository)
    {
        repository = null!;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
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
            return false;
        }

        repository = new GitHubRepository(
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

        return true;
    }
}
