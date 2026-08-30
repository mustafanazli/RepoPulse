using System.Globalization;
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

    Task<GitHubLatestCommitResult> GetLatestRepositoryCommitAsync(string accessToken, string owner, string repository, CancellationToken cancellationToken);

    // RP-016: counts commits reachable from the repository's DEFAULT branch
    // only — GitHub's /commits endpoint without an explicit sha/ref query
    // parameter walks the default branch's history, never every branch's
    // combined activity. sinceUtc/untilUtc are always supplied by the
    // caller (no system clock read here); the window is a plain half-open
    // range [sinceUtc, untilUtc) with no fixed 30/90-day assumption baked in.
    Task<GitHubCommitCountResult> GetDefaultBranchCommitCountAsync(
        string accessToken,
        string owner,
        string repository,
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        CancellationToken cancellationToken);
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

    // RP-013: GET /repos/{owner}/{repository}/commits?per_page=1 — a single
    // record is always sufficient (GitHub returns commits newest-first by
    // default), so no pagination/Link-header handling exists here, unlike
    // GetUserRepositoriesAsync. Owner and repository are always the two
    // fields of an already-fetched GitHubRepository — never raw user input —
    // and are still percent-encoded here as defense in depth, exactly like
    // GetRepositoryAsync.
    public async Task<GitHubLatestCommitResult> GetLatestRepositoryCommitAsync(string accessToken, string owner, string repository, CancellationToken cancellationToken)
    {
        var requestUri = $"{OAuthConstants.RepositoryEndpointBase}/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/commits?per_page=1";

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
            return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.NetworkError);
        }
        catch (HttpRequestException)
        {
            return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.NetworkError);
        }
        // See the matching comment in RepoPulseAuthApiClient.ExchangeAsync —
        // Xamarin.Android's HTTP handler can surface a raw socket failure as
        // System.Net.WebException instead of HttpRequestException.
        catch (WebException)
        {
            return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.NetworkError);
        }

        // Status code alone decides the outcome — GitHub's own response body
        // (which may carry an error message) is never surfaced to the
        // caller/UI/logs for any non-success status. GitHub returns 409 for
        // a genuinely empty repository's commits endpoint — treated as a
        // real success (zero commits), not a failure.
        switch (statusCode)
        {
            case HttpStatusCode.OK:
                return ParseLatestCommit(body);
            case (HttpStatusCode)409:
                return GitHubLatestCommitResult.NoCommits();
            case HttpStatusCode.NotFound:
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.NotFound);
            case HttpStatusCode.Unauthorized:
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.Unauthorized);
            case HttpStatusCode.Forbidden:
            case (HttpStatusCode)429:
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.RateLimited);
            default:
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.Unexpected);
        }
    }

    // RP-016: GET /repos/{owner}/{repository}/commits?since=..&until=..&per_page=1
    // — counts commits in a caller-supplied UTC window WITHOUT downloading
    // every commit body. per_page=1 forces GitHub to paginate at one record
    // per page; the total commit count is then read from the page number in
    // the response's Link "rel=last" entry (RFC 8288), never by following
    // that URL with a second request. Only the DEFAULT branch is counted —
    // GitHub's own default for this endpoint when no sha/ref is given.
    //
    // sinceUtc/untilUtc are validated by the caller of this method (never
    // read from the system clock here) and are always normalized to UTC and
    // sent as an ISO 8601 round-trip ("o") timestamp — offset-independent,
    // so two DateTimeOffset values representing the same instant with
    // different offsets produce byte-identical query values.
    public async Task<GitHubCommitCountResult> GetDefaultBranchCommitCountAsync(
        string accessToken,
        string owner,
        string repository,
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc,
        CancellationToken cancellationToken)
    {
        if (sinceUtc >= untilUtc)
        {
            // Resolved before any network call — an invalid range is a
            // caller-input problem, not something GitHub needs to reject.
            return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.InvalidRange);
        }

        var sinceQueryValue = Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        var untilQueryValue = Uri.EscapeDataString(untilUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        var requestUri =
            $"{OAuthConstants.RepositoryEndpointBase}/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/commits" +
            $"?since={sinceQueryValue}&until={untilQueryValue}&per_page=1";

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
            return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.NetworkError);
        }
        catch (HttpRequestException)
        {
            return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.NetworkError);
        }
        // See the matching comment in RepoPulseAuthApiClient.ExchangeAsync —
        // Xamarin.Android's HTTP handler can surface a raw socket failure as
        // System.Net.WebException instead of HttpRequestException.
        catch (WebException)
        {
            return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.NetworkError);
        }

        // Status code alone decides the outcome — GitHub's own response body
        // (which may carry an error message) is never surfaced to the
        // caller/UI/logs for any non-success status. GitHub returns 409 for
        // a genuinely empty repository's commits endpoint — treated as a
        // real success (zero commits), not a failure, exactly like
        // GetLatestRepositoryCommitAsync.
        switch (statusCode)
        {
            case HttpStatusCode.OK:
                return ParseCommitCount(body, linkHeaderValue, owner, repository, sinceUtc, untilUtc);
            case (HttpStatusCode)409:
                return GitHubCommitCountResult.Success(0);
            case HttpStatusCode.NotFound:
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.NotFound);
            case HttpStatusCode.Unauthorized:
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unauthorized);
            case HttpStatusCode.Forbidden:
            case (HttpStatusCode)429:
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.RateLimited);
            default:
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
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

    // The endpoint always returns a JSON array (newest commit first); this
    // reads only element [0] and never tracks pagination — a single record
    // is the entire contract. committer.date is tried first (when a commit
    // is authored on one machine and committed/pushed from another, or
    // rebased, committer.date reflects when it actually entered the
    // repository's history — the more accurate "last activity" signal);
    // author.date is a fallback only when committer.date is missing or
    // unparsable. If neither date is present, this is malformed data, not a
    // repository with a valid but unknown commit time — Unexpected, never a
    // fabricated/guessed date, and never pushed_at/updated_at as a
    // substitute (a repo can be pushed to, e.g. a tag, without a new
    // commit).
    private static GitHubLatestCommitResult ParseLatestCommit(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.Unexpected);
            }

            if (root.GetArrayLength() == 0)
            {
                return GitHubLatestCommitResult.NoCommits();
            }

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Object ||
                !first.TryGetProperty("commit", out var commitElement) ||
                commitElement.ValueKind != JsonValueKind.Object)
            {
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.Unexpected);
            }

            var committedAt = TryGetPersonDate(commitElement, "committer") ?? TryGetPersonDate(commitElement, "author");
            if (committedAt is null)
            {
                return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.Unexpected);
            }

            // Deliberately reads nothing else from `first`/`commitElement` —
            // no sha, no commit message. RepositoryDetailPage shows only the
            // date; GitHub's commit SHA/message are real content the app has
            // no UI use for, so they are never extracted, retained, or able
            // to reach a log/exception/route in the first place (data
            // minimization — see GitHubLatestCommit's doc comment).
            return GitHubLatestCommitResult.Success(new GitHubLatestCommit(committedAt.Value));
        }
        catch (JsonException)
        {
            return GitHubLatestCommitResult.Failure(GitHubLatestCommitFailureKind.Unexpected);
        }
    }

    private static DateTimeOffset? TryGetPersonDate(JsonElement commitElement, string personPropertyName) =>
        commitElement.TryGetProperty(personPropertyName, out var person) &&
        person.ValueKind == JsonValueKind.Object &&
        person.TryGetProperty("date", out var dateElement) &&
        dateElement.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(dateElement.GetString(), out var parsed)
            ? parsed
            : null;

    // RP-016: interprets a single per_page=1 commits response. The only
    // trustworthy source of the total count beyond "0" or "1" is a
    // validated rel="last" entry in the Link header — its page number is
    // the count, since each page holds exactly one commit. Any shape that
    // cannot be safely interpreted (more than one record despite per_page=1,
    // an empty array alongside a Link header, a missing/duplicate/untrusted
    // rel="last") returns Unexpected rather than guessing.
    private static GitHubCommitCountResult ParseCommitCount(
        string body,
        string? linkHeaderValue,
        string owner,
        string repository,
        DateTimeOffset sinceUtc,
        DateTimeOffset untilUtc)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
            }

            var length = root.GetArrayLength();

            if (length == 0)
            {
                // GitHub would never report more pages exist (a Link header)
                // for a page that came back empty — that contradiction is
                // treated as untrusted data, not "zero commits".
                return string.IsNullOrEmpty(linkHeaderValue)
                    ? GitHubCommitCountResult.Success(0)
                    : GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
            }

            if (length > 1)
            {
                // per_page=1 was requested; more than one record back is a
                // contract violation, never silently truncated to the first.
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
            }

            // A JSON array of length 1 only proves something is present — not
            // that it is a commit. Before this element is trusted enough to
            // report Success(1), or to let a Link header's rel="last" page
            // number be read at all, it must have the minimum shape of a
            // GitHub commit-list item: an object with a non-empty string
            // "sha" and an object "commit". Nothing beyond that shape is
            // read — no sha value, message, author/committer, or date is
            // extracted, retained, or ever reaches the result/log/exception.
            if (!HasMinimalCommitListItemShape(root[0]))
            {
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
            }

            if (string.IsNullOrEmpty(linkHeaderValue))
            {
                // No Link header at all is GitHub's own signal that this is
                // the only page — consistent with per_page=1 pagination
                // semantics, a single validated item with no further pages
                // means exactly one commit exists in the window.
                return GitHubCommitCountResult.Success(1);
            }

            var (found, lastUrl) = TryFindLastLinkUrl(linkHeaderValue);
            if (!found)
            {
                // Either no rel="last" entry at all, or more than one
                // (conflicting/duplicate) — never guessed either way.
                return GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected);
            }

            var page = ValidateAndExtractLastPage(lastUrl!, owner, repository, sinceUtc, untilUtc);
            return page is null
                ? GitHubCommitCountResult.Failure(GitHubCommitCountFailureKind.Unexpected)
                : GitHubCommitCountResult.Success(page.Value);
        }
    }

    // Minimum shape check for a single element of a /commits response array —
    // deliberately shallow: this only needs to distinguish "a real commit
    // record" from "not a commit at all" (null/string/number/bool/array/an
    // empty or unrelated object), never to validate the commit's content.
    // "sha" is checked only for being a non-empty/non-whitespace string —
    // no length or hex-format requirement, since Git's object ID format is
    // not this method's contract to enforce and could change. The sha value
    // itself, and everything inside "commit", is never read any further.
    private static bool HasMinimalCommitListItemShape(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("sha", out var sha) &&
        sha.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(sha.GetString()) &&
        element.TryGetProperty("commit", out var commit) &&
        commit.ValueKind == JsonValueKind.Object;

    // Finds the rel="last" entry in an RFC 8288 Link header. Returns
    // Found=false both when no such entry exists AND when more than one
    // conflicting rel="last" entry is present — both cases mean "nothing
    // trustworthy to read a count from", and the caller treats them
    // identically (Unexpected), never guessing which duplicate to trust.
    private static (bool Found, string? Url) TryFindLastLinkUrl(string linkHeaderValue)
    {
        string? lastUrl = null;
        var matchCount = 0;

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
            if (!attributes.Contains("rel=\"last\"", StringComparison.Ordinal))
            {
                continue;
            }

            matchCount++;
            lastUrl = segment[(linkStart + 1)..linkEnd];
        }

        return matchCount == 1 ? (true, lastUrl) : (false, null);
    }

    // Validates a candidate rel="last" URL against a strict trust anchor
    // before ever reading its "page" value as a count: exact scheme/host/
    // default port/no userinfo/no fragment/exact path, and a query
    // containing ONLY since/until/per_page/page — no duplicates, no empty
    // key/value, no unknown parameter. since/until are compared as the SAME
    // UTC INSTANT as the original request (DateTimeOffset equality is
    // offset-independent), not as raw strings, so URL-encoding or
    // offset-notation differences between the original request and this
    // link are tolerated as long as they name the same point in time.
    private static int? ValidateAndExtractLastPage(
        string url,
        string owner,
        string repository,
        DateTimeOffset expectedSinceUtc,
        DateTimeOffset expectedUntilUtc)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var expectedPath = $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/commits";
        if (uri.AbsolutePath != expectedPath)
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
        var sawSince = false;
        var sawUntil = false;
        var sawPerPage = false;

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
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPage) || parsedPage <= 0)
                    {
                        return null;
                    }
                    page = parsedPage;
                    break;
                case "per_page":
                    if (value != "1")
                    {
                        return null;
                    }
                    sawPerPage = true;
                    break;
                case "since":
                    if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var since) ||
                        since != expectedSinceUtc)
                    {
                        return null;
                    }
                    sawSince = true;
                    break;
                case "until":
                    if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) ||
                        until != expectedUntilUtc)
                    {
                        return null;
                    }
                    sawUntil = true;
                    break;
                default:
                    // Any parameter outside the expected set makes this
                    // "last" link untrusted.
                    return null;
            }
        }

        return page is not null && sawSince && sawUntil && sawPerPage ? page : null;
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
