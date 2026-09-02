using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// RP-020: GitHubApiClient.GetOldestOpenIssueAsync — a single GraphQL POST for
// the oldest OPEN issue's creation time, the first data-collection slice for
// the future Bakım (maintenance) sub-score. No test here ever contacts the
// real GitHub network — every call goes through FakeHttpMessageHandler /
// ThrowingHttpMessageHandler.
public class GitHubOldestOpenIssueQueryTests
{
    private const string Token = "test-access-token";
    private const string Owner = "mustafanazli";
    private const string Repository = "RepoPulse";

    private static GitHubApiClient MakeClient(HttpMessageHandler handler) => new(new HttpClient(handler));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string SuccessBody(string createdAtIso, int totalCount = 1) =>
        new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["repository"] = new JsonObject
                {
                    ["issues"] = new JsonObject
                    {
                        ["totalCount"] = totalCount,
                        ["nodes"] = new JsonArray(new JsonObject { ["createdAt"] = createdAtIso })
                    }
                }
            }
        }.ToJsonString();

    private static string NoOpenIssuesBody() =>
        """{"data":{"repository":{"issues":{"totalCount":0,"nodes":[]}}}}""";

    private static string RepositoryNullBody() =>
        """{"data":{"repository":null}}""";

    private static string GraphQlErrorsBody(string type) =>
        new JsonObject
        {
            ["errors"] = new JsonArray(new JsonObject
            {
                ["type"] = type,
                ["message"] = "do not leak this text"
            })
        }.ToJsonString();

    // ===================== A. Request contract =====================

    [Fact]
    public async Task SendsExactlyOneRequestToGraphQlEndpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var client = MakeClient(handler);

        await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.github.com/graphql", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendsTokenOnlyInAuthorizationHeaderNeverInBody()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var client = MakeClient(handler);

        await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Token, request.Headers.Authorization.Parameter);
        Assert.DoesNotContain(Token, handler.LastRequestBody);
    }

    [Fact]
    public async Task QueryTextContainsRepositoryIssuesOpenFirstOneCreatedAtAscending()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var client = MakeClient(handler);

        await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        var body = handler.LastRequestBody!;
        Assert.Contains("repository(owner: $owner, name: $name)", body, StringComparison.Ordinal);
        Assert.Contains("states: OPEN", body, StringComparison.Ordinal);
        Assert.Contains("first: 1", body, StringComparison.Ordinal);
        Assert.Contains("CREATED_AT", body, StringComparison.Ordinal);
        Assert.Contains("direction: ASC", body, StringComparison.Ordinal);
        Assert.Contains("totalCount", body, StringComparison.Ordinal);
        Assert.Contains("createdAt", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryTextIsStaticRegardlessOfOwnerAndRepositoryValues()
    {
        var handlerA = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var clientA = MakeClient(handlerA);
        var handlerB = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var clientB = MakeClient(handlerB);

        await clientA.GetOldestOpenIssueAsync(Token, "owner-one", "repo-one", CancellationToken.None);
        await clientB.GetOldestOpenIssueAsync(Token, "a-completely-different-owner", "and-a-different-repo", CancellationToken.None);

        var queryA = ExtractJsonStringField(handlerA.LastRequestBody!, "query");
        var queryB = ExtractJsonStringField(handlerB.LastRequestBody!, "query");
        Assert.Equal(queryA, queryB);
    }

    [Fact]
    public async Task VariablesContainOwnerAndNameExactly()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var client = MakeClient(handler);

        await client.GetOldestOpenIssueAsync(Token, "some-owner", "some-repo", CancellationToken.None);

        using var document = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var variables = document.RootElement.GetProperty("variables");
        Assert.Equal("some-owner", variables.GetProperty("owner").GetString());
        Assert.Equal("some-repo", variables.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SpecialCharactersInOwnerOrRepositoryCannotAlterQueryShape()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var client = MakeClient(handler);

        const string maliciousOwner = "o\"}) { __typename } mutation { deleteRepo(name: \"x";
        const string maliciousRepo = "r\\\"\n\t{evil}";

        await client.GetOldestOpenIssueAsync(Token, maliciousOwner, maliciousRepo, CancellationToken.None);

        // The request body must still be valid JSON (the malicious strings
        // were properly escaped inside the "variables" object) and the query
        // text itself must still be exactly the static query — proving the
        // injected content landed only inside a JSON string value, never
        // altering the query's structure.
        using var document = System.Text.Json.JsonDocument.Parse(handler.LastRequestBody!);
        var variables = document.RootElement.GetProperty("variables");
        Assert.Equal(maliciousOwner, variables.GetProperty("owner").GetString());
        Assert.Equal(maliciousRepo, variables.GetProperty("name").GetString());

        var query = document.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("repository(owner: $owner, name: $name)", query, StringComparison.Ordinal);
        Assert.DoesNotContain("deleteRepo", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsStandardGitHubHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-01-01T00:00:00Z")));
        var client = MakeClient(handler);

        await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("RepoPulse", request.Headers.UserAgent.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
    }

    // Minimal helper to pull the raw "query" string field out of the
    // top-level GraphQL request JSON for byte-identity comparison — reads
    // only that one field, deliberately not a full JSON parser.
    private static string ExtractJsonStringField(string json, string fieldName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty(fieldName).GetString()!;
    }

    // ===================== B. Success =====================

    [Fact]
    public async Task TotalCountOneWithValidCreatedAt_ReturnsSuccess()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2025-03-15T10:30:00Z")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.HasOpenIssues);
        Assert.Null(result.FailureKind);
        Assert.Equal(new DateTimeOffset(2025, 3, 15, 10, 30, 0, TimeSpan.Zero), result.CreatedAtUtc);
    }

    [Fact]
    public async Task TotalCountGreaterThanOneWithSingleNode_ReturnsSuccessWithThatNodesDate()
    {
        // GitHub's own first:1 page size guarantees at most one node
        // regardless of how large totalCount is — this proves the parser
        // trusts the actual nodes array, not totalCount, for the returned
        // date.
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2024-06-01T00:00:00Z", totalCount: 47)));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), result.CreatedAtUtc);
    }

    [Fact]
    public async Task DifferentDateTimeOffsetNotations_ParseToSameInstant()
    {
        var handlerUtc = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2025-01-01T12:00:00Z")));
        var clientUtc = MakeClient(handlerUtc);
        var handlerOffset = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2025-01-01T14:00:00+02:00")));
        var clientOffset = MakeClient(handlerOffset);

        var resultUtc = await clientUtc.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);
        var resultOffset = await clientOffset.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.Equal(resultUtc.CreatedAtUtc, resultOffset.CreatedAtUtc);
        // Instant-equality alone (DateTimeOffset.Equals) does not prove
        // normalization — a +02:00 value and a Z value represent the same
        // instant even without ever being converted. Both offsets must
        // independently be verified as exactly zero.
        Assert.Equal(TimeSpan.Zero, resultUtc.CreatedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, resultOffset.CreatedAtUtc!.Value.Offset);
    }

    // ===================== B2. UTC normalization =====================

    [Fact]
    public async Task PositiveOffsetCreatedAt_NormalizesInstantAndOffsetToUtc()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-08-30T15:00:00+03:00")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), result.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.CreatedAtUtc!.Value.Offset);
    }

    [Fact]
    public async Task NegativeOffsetCreatedAt_NormalizesInstantAndOffsetToUtc()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-08-30T05:00:00-05:00")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero), result.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.CreatedAtUtc!.Value.Offset);
    }

    [Fact]
    public async Task ZSuffixCreatedAt_OffsetIsZero()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2026-08-30T12:00:00Z")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.Zero, result.CreatedAtUtc!.Value.Offset);
    }

    // ===================== C. No open issues =====================

    [Fact]
    public async Task TotalCountZeroWithEmptyNodes_ReturnsNoOpenIssues()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, NoOpenIssuesBody()));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.HasOpenIssues);
        Assert.Null(result.CreatedAtUtc);
        Assert.Null(result.FailureKind);
    }

    [Fact]
    public async Task NoOpenIssues_IsNotConflatedWithFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, NoOpenIssuesBody()));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.FailureKind);
    }

    // ===================== D. Repository unavailable =====================

    [Fact]
    public async Task DataRepositoryNull_ReturnsRepositoryUnavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, RepositoryNullBody()));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.RepositoryUnavailable, result.FailureKind);
    }

    // ===================== E. HTTP failure mapping =====================

    [Fact]
    public async Task Http401_ReturnsUnauthorized()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Unauthorized, "{}"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unauthorized, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Http403_ReturnsRateLimited()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Forbidden, "{}"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.RateLimited, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Http429_ReturnsRateLimited()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse((HttpStatusCode)429, "{}"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.RateLimited, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Http404_ReturnsUnexpected()
    {
        // GraphQL does not use 404 to mean "repository not found" (that
        // comes back as data.repository == null inside a 200) — a genuine
        // 404 from this endpoint is an unrecognized transport-layer shape,
        // documented here as Unexpected rather than guessed at.
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound, "{}"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    // Uses FakeHttpMessageHandler (not ThrowingHttpMessageHandler) with a
    // throwing responder — FakeHttpMessageHandler records the request into
    // Requests/RequestCount BEFORE invoking the responder, so RequestCount
    // is observable even when the responder itself throws. This proves
    // exactly one request was attempted, not zero and not a retry.
    [Fact]
    public async Task HttpRequestException_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated failure"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.NetworkError, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WebException_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new WebException("Socket closed"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.NetworkError, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Timeout_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("simulated timeout"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.NetworkError, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CallersOwnTokenCancelled_RethrowsRatherThanSwallowing()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new OperationCanceledException("simulated own-token timeout"));
        var client = MakeClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetOldestOpenIssueAsync(Token, Owner, Repository, cts.Token));

        // Deterministic RequestCount for this scenario, empirically pinned:
        // HttpClient does NOT short-circuit on an already-cancelled caller
        // token before dispatching to the handler — SendAsync still runs
        // (and throws), so exactly one request attempt is recorded, not
        // zero and not a retry.
        Assert.Equal(1, handler.RequestCount);
    }

    // ===================== F. GraphQL errors =====================

    [Fact]
    public async Task Http200WithErrors_IsNeverSuccess()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, GraphQlErrorsBody("SOME_OTHER_ERROR")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task StructuredRateLimitError_ReturnsRateLimited()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, GraphQlErrorsBody("RATE_LIMITED")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.RateLimited, result.FailureKind);
    }

    [Theory]
    [InlineData("FORBIDDEN")]
    [InlineData("INSUFFICIENT_SCOPES")]
    [InlineData("UNAUTHORIZED")]
    public async Task StructuredAuthorizationError_ReturnsUnauthorized(string errorType)
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, GraphQlErrorsBody(errorType)));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unauthorized, result.FailureKind);
    }

    [Fact]
    public async Task UnknownGraphQlErrorType_ReturnsUnexpected()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, GraphQlErrorsBody("SOME_UNRECOGNIZED_TYPE")));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
    }

    [Fact]
    public async Task DataAndErrorsTogether_FailsClosedNeverPartialSuccess()
    {
        var body = """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[{"createdAt":"2025-01-01T00:00:00Z"}]}}},"errors":[{"type":"RATE_LIMITED","message":"do not leak"}]}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.RateLimited, result.FailureKind);
        Assert.Null(result.CreatedAtUtc);
        Assert.Equal(1, handler.RequestCount);
    }

    // ===================== F2. Empty/non-array errors =====================
    //
    // An `errors` property that is syntactically present but carries no
    // actual error (an empty array) must not block otherwise-valid `data`
    // from being processed — this is the RP-020 hardening fix: previously,
    // ANY presence of the `errors` key (even `[]`) short-circuited straight
    // to Unexpected before `data` was ever read.

    [Fact]
    public async Task EmptyErrorsArrayWithValidIssueData_ReturnsSuccess()
    {
        var body = """{"errors":[],"data":{"repository":{"issues":{"totalCount":1,"nodes":[{"createdAt":"2025-06-01T00:00:00Z"}]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), result.CreatedAtUtc);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EmptyErrorsArrayWithNoOpenIssuesData_ReturnsNoOpenIssues()
    {
        var body = """{"errors":[],"data":{"repository":{"issues":{"totalCount":0,"nodes":[]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.HasOpenIssues);
        Assert.Null(result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EmptyErrorsArrayWithRepositoryNull_ReturnsRepositoryUnavailable()
    {
        var body = """{"errors":[],"data":{"repository":null}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.RepositoryUnavailable, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EmptyErrorsArrayWithMalformedData_ReturnsUnexpected()
    {
        var body = """{"errors":[],"data":{"repository":{"issues":{"totalCount":1,"nodes":[]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NullErrorsWithValidData_ReturnsUnexpected()
    {
        var body = """{"errors":null,"data":{"repository":{"issues":{"totalCount":1,"nodes":[{"createdAt":"2025-06-01T00:00:00Z"}]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("\"a string\"")]
    public async Task NonArrayErrorsShapeWithValidData_ReturnsUnexpected(string errorsValueJson)
    {
        var body = "{\"errors\":" + errorsValueJson +
            ",\"data\":{\"repository\":{\"issues\":{\"totalCount\":1,\"nodes\":[{\"createdAt\":\"2025-06-01T00:00:00Z\"}]}}}}";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    // ===================== G. Response shape =====================

    public static IEnumerable<object[]> MalformedResponseBodies()
    {
        yield return new object[] { "not valid json at all", "malformed JSON" };
        yield return new object[] { "[]", "root is an array, not an object" };
        yield return new object[] { "\"just a string\"", "root is a string" };
        yield return new object[] { "42", "root is a number" };
        yield return new object[] { "{}", "data missing entirely" };
        yield return new object[] { """{"data":null}""", "data is null" };
        yield return new object[] { """{"data":"not-an-object"}""", "data is a non-object" };
        yield return new object[] { """{"data":{}}""", "repository missing entirely" };
        yield return new object[] { """{"data":{"repository":"not-an-object"}}""", "repository is a non-object (non-null)" };
        yield return new object[] { """{"data":{"repository":{}}}""", "issues missing entirely" };
        yield return new object[] { """{"data":{"repository":{"issues":null}}}""", "issues is null" };
        yield return new object[] { """{"data":{"repository":{"issues":"nope"}}}""", "issues is a non-object" };
        yield return new object[] { """{"data":{"repository":{"issues":{"nodes":[]}}}}""", "totalCount missing entirely" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":null,"nodes":[]}}}}""", "totalCount is null" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":"1","nodes":[]}}}}""", "totalCount is a string" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":-1,"nodes":[]}}}}""", "totalCount is negative" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":0}}}}""", "nodes missing entirely" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":0,"nodes":null}}}}""", "nodes is null" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":0,"nodes":"nope"}}}}""", "nodes is a non-array" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":0,"nodes":[{"createdAt":"2025-01-01T00:00:00Z"}]}}}}""", "totalCount=0 but a node is present" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[]}}}}""", "totalCount>0 but nodes is empty" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[null]}}}}""", "node is null" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":["oops"]}}}}""", "node is a string" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[42]}}}}""", "node is a number" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[[]]}}}}""", "node is an array" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":2,"nodes":[{"createdAt":"2025-01-01T00:00:00Z"},{"createdAt":"2025-01-02T00:00:00Z"}]}}}}""", "more than one node" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[{}]}}}}""", "createdAt missing entirely" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[{"createdAt":null}]}}}}""", "createdAt is null" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[{"createdAt":""}]}}}}""", "createdAt is empty string" };
        yield return new object[] { """{"data":{"repository":{"issues":{"totalCount":1,"nodes":[{"createdAt":"not-a-date"}]}}}}""", "createdAt is not a valid date" };
    }

    [Theory]
    [MemberData(nameof(MalformedResponseBodies))]
    public async Task MalformedOrInconsistentResponseShapes_ReturnUnexpectedWithSingleRequest(string body, string scenario)
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
        _ = scenario; // documents intent in the test explorer; not asserted on
    }

    // ===================== G2. totalCount boundary =====================

    [Fact]
    public async Task TotalCountAtInt32MaxValue_WithSingleValidNode_ReturnsSuccess()
    {
        // int.MaxValue is shape-valid — a plain JSON integer that fits in
        // Int32 — and GraphQL's own first:1 page size still guarantees at
        // most one node, so Success semantics must not break just because
        // totalCount is enormous.
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SuccessBody("2025-06-01T00:00:00Z", totalCount: int.MaxValue)));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), result.CreatedAtUtc);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TotalCountOneMoreThanInt32MaxValue_ReturnsUnexpected()
    {
        // 2147483648 does not fit in Int32 — JsonElement.TryGetInt32 must
        // fail, and the parser must not silently truncate/wrap it.
        var body = """{"data":{"repository":{"issues":{"totalCount":2147483648,"nodes":[{"createdAt":"2025-06-01T00:00:00Z"}]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TotalCountFarExceedingJsonIntegerRange_ReturnsUnexpected()
    {
        var body = """{"data":{"repository":{"issues":{"totalCount":99999999999999999999999999999,"nodes":[{"createdAt":"2025-06-01T00:00:00Z"}]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task FractionalTotalCount_ReturnsUnexpected()
    {
        var body = """{"data":{"repository":{"issues":{"totalCount":1.5,"nodes":[{"createdAt":"2025-06-01T00:00:00Z"}]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NegativeTotalCount_ReturnsUnexpected()
    {
        // Regression pin for the existing negative-totalCount case already
        // covered by MalformedResponseBodies — kept here as its own
        // dedicated, explicitly-named test per the totalCount boundary
        // requirement, with the same RequestCount assertion.
        var body = """{"data":{"repository":{"issues":{"totalCount":-1,"nodes":[]}}}}""";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    // ===================== Input validation (no network call) =====================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceAccessToken_RejectedWithoutNetworkCall(string accessToken)
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should never be called"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(accessToken, Owner, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("", Repository)]
    [InlineData("   ", Repository)]
    [InlineData(Owner, "")]
    [InlineData(Owner, "   ")]
    public async Task EmptyOrWhitespaceOwnerOrRepository_RejectedWithoutNetworkCall(string owner, string repository)
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should never be called"));
        var client = MakeClient(handler);

        var result = await client.GetOldestOpenIssueAsync(Token, owner, repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExcessivelyLongOwnerOrRepository_RejectedWithoutNetworkCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should never be called"));
        var client = MakeClient(handler);
        var tooLong = new string('a', 500);

        var result = await client.GetOldestOpenIssueAsync(Token, tooLong, Repository, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubOldestOpenIssueFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(0, handler.RequestCount);
    }

    // ===================== H. Result invariant / reflection =====================

    [Fact]
    public void Result_HasNoPublicConstructor()
    {
        var publicConstructors = typeof(GitHubOldestOpenIssueResult).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Result_HasNoGenericOrArbitraryCreateFactory()
    {
        var factoryMethods = typeof(GitHubOldestOpenIssueResult)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.DoesNotContain("Create", factoryMethods);
        Assert.Equal(new HashSet<string> { "Success", "NoOpenIssues", "Failure" }, factoryMethods);
    }

    [Fact]
    public void SuccessFactory_ProducesOnlyTheCanonicalShape()
    {
        var result = GitHubOldestOpenIssueResult.Success(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(result.IsSuccess);
        Assert.True(result.HasOpenIssues);
        Assert.NotNull(result.CreatedAtUtc);
        Assert.Null(result.FailureKind);
        Assert.Equal(TimeSpan.Zero, result.CreatedAtUtc!.Value.Offset);
    }

    // The invariant is enforced by the factory itself, not merely by the one
    // current caller (GitHubApiClient's parser) happening to always pass a
    // UTC value — this proves it holds even when called directly with a
    // non-UTC DateTimeOffset, which is the only way any caller (in this
    // assembly or elsewhere) could ever construct a GitHubOldestOpenIssueResult.
    [Theory]
    [InlineData(3)]
    [InlineData(-5)]
    [InlineData(9)]
    [InlineData(-11)]
    public void SuccessFactory_NormalizesAnyOffsetToUtc(int offsetHours)
    {
        var nonUtcValue = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.FromHours(offsetHours));

        var result = GitHubOldestOpenIssueResult.Success(nonUtcValue);

        Assert.Equal(TimeSpan.Zero, result.CreatedAtUtc!.Value.Offset);
        Assert.Equal(nonUtcValue.ToUniversalTime(), result.CreatedAtUtc);
    }

    [Fact]
    public void NoOpenIssuesFactory_ProducesOnlyTheCanonicalShape()
    {
        var result = GitHubOldestOpenIssueResult.NoOpenIssues();

        Assert.True(result.IsSuccess);
        Assert.False(result.HasOpenIssues);
        Assert.Null(result.CreatedAtUtc);
        Assert.Null(result.FailureKind);
    }

    [Theory]
    [InlineData(GitHubOldestOpenIssueFailureKind.RepositoryUnavailable)]
    [InlineData(GitHubOldestOpenIssueFailureKind.Unauthorized)]
    [InlineData(GitHubOldestOpenIssueFailureKind.RateLimited)]
    [InlineData(GitHubOldestOpenIssueFailureKind.NetworkError)]
    [InlineData(GitHubOldestOpenIssueFailureKind.Unexpected)]
    public void FailureFactory_ProducesOnlyTheCanonicalShape(GitHubOldestOpenIssueFailureKind failureKind)
    {
        var result = GitHubOldestOpenIssueResult.Failure(failureKind);

        Assert.False(result.IsSuccess);
        Assert.False(result.HasOpenIssues);
        Assert.Null(result.CreatedAtUtc);
        Assert.Equal(failureKind, result.FailureKind);
    }

    [Fact]
    public void Result_CarriesNoTokenSessionBodyHeaderQueryOrUrlField()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Body", "Header", "Query", "Url", "Client", "Secret", "Http", "Message" };

        var properties = typeof(GitHubOldestOpenIssueResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        foreach (var name in properties)
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Result_IsSealed()
    {
        Assert.True(typeof(GitHubOldestOpenIssueResult).IsSealed);
    }
}
