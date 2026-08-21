using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoPulse.Core.Authentication;

public interface IRepoPulseAuthApiClient
{
    Task<AuthApiExchangeResult> ExchangeAsync(string code, string codeVerifier, CancellationToken cancellationToken);
}

// Talks only to our own backend's token-exchange endpoint — never to GitHub
// directly (see ADR-003 / RP-005: GitHub's classic OAuth App type requires a
// client_secret at token-exchange time that a mobile app cannot hold safely).
// Sends only { code, codeVerifier } — client_id/client_secret/redirect_uri/
// state/tokenEndpoint are never part of this request; the backend supplies
// them itself from its own trusted configuration.
public sealed class RepoPulseAuthApiClient : IRepoPulseAuthApiClient
{
    private const string ExchangePath = "/oauth/github/exchange";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public RepoPulseAuthApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<AuthApiExchangeResult> ExchangeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ExchangePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("RepoPulse");
        request.Content = JsonContent.Create(new ExchangeRequestBody(code, codeVerifier), options: JsonOptions);

        HttpStatusCode statusCode;
        string body;
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
            return AuthApiExchangeResult.Failure(AuthApiExchangeFailureKind.Timeout);
        }
        catch (HttpRequestException)
        {
            return AuthApiExchangeResult.Failure(AuthApiExchangeFailureKind.NetworkError);
        }

        if (statusCode == HttpStatusCode.OK)
        {
            return ParseSuccess(body);
        }

        return AuthApiExchangeResult.Failure(MapFailureKind(statusCode, TryReadProblemTitle(body)));
    }

    private static AuthApiExchangeResult ParseSuccess(string body)
    {
        SuccessBody? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SuccessBody>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return AuthApiExchangeResult.Failure(AuthApiExchangeFailureKind.MalformedResponse);
        }

        if (parsed is null || string.IsNullOrEmpty(parsed.AccessToken))
        {
            return AuthApiExchangeResult.Failure(AuthApiExchangeFailureKind.MalformedResponse);
        }

        return AuthApiExchangeResult.Ok(new AuthApiTokenResponse(
            parsed.AccessToken,
            string.IsNullOrEmpty(parsed.TokenType) ? "bearer" : parsed.TokenType,
            parsed.Scope,
            parsed.ExpiresIn,
            parsed.RefreshToken,
            parsed.RefreshTokenExpiresIn));
    }

    private static string? TryReadProblemTitle(string body)
    {
        try
        {
            var problem = JsonSerializer.Deserialize<ProblemBody>(body, JsonOptions);
            return problem?.Title;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AuthApiExchangeFailureKind MapFailureKind(HttpStatusCode statusCode, string? title) => title switch
    {
        "invalid_request" => AuthApiExchangeFailureKind.InvalidRequest,
        "oauth_exchange_failed" => AuthApiExchangeFailureKind.OAuthExchangeFailed,
        "upstream_error" => AuthApiExchangeFailureKind.UpstreamError,
        "upstream_timeout" => AuthApiExchangeFailureKind.UpstreamTimeout,
        "rate_limited" => AuthApiExchangeFailureKind.RateLimited,
        "internal_error" => AuthApiExchangeFailureKind.InternalError,
        _ => statusCode switch
        {
            HttpStatusCode.BadRequest => AuthApiExchangeFailureKind.InvalidRequest,
            HttpStatusCode.TooManyRequests => AuthApiExchangeFailureKind.RateLimited,
            HttpStatusCode.BadGateway => AuthApiExchangeFailureKind.UpstreamError,
            HttpStatusCode.GatewayTimeout => AuthApiExchangeFailureKind.UpstreamTimeout,
            _ => AuthApiExchangeFailureKind.InternalError
        }
    };

    private sealed record ExchangeRequestBody(string Code, string CodeVerifier);

    private sealed class SuccessBody
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("tokenType")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("expiresIn")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("refreshTokenExpiresIn")]
        public long? RefreshTokenExpiresIn { get; set; }
    }

    private sealed class ProblemBody
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}
