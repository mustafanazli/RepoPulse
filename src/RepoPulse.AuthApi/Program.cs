using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RepoPulse.AuthApi.Configuration;
using RepoPulse.AuthApi.Contracts;
using RepoPulse.AuthApi.GitHub;

const string OAuthExchangePath = "/oauth/github/exchange";
const string RateLimiterPolicyName = "oauth-exchange";

// Small, fixed cap — this endpoint only ever needs two short string fields.
// Enforced at the application level (not just Kestrel's own limit) so it is
// exercised the same way under the in-memory TestServer used by tests.
const long MaxOAuthExchangeRequestBodyBytes = 4096;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<GitHubOAuthOptions>()
    .Bind(builder.Configuration.GetSection(GitHubOAuthOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<GitHubOAuthOptions>, GitHubOAuthOptionsValidator>();

// Server-side only — bound once at startup from configuration, never from
// any request. See HostingOptions.cs and docs/adr/004-production-hosting.md.
builder.Services
    .AddOptions<HostingOptions>()
    .Bind(builder.Configuration.GetSection(HostingOptions.SectionName));

builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
{
    // Unknown fields (e.g. an attacker-supplied "clientSecret" or
    // "redirectUri") must be rejected outright, not silently ignored.
    jsonOptions.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

builder.Services
    .AddHttpClient<IGitHubTokenExchangeService, GitHubTokenExchangeService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned per client IP: a fixed window keeps this simple and
    // sufficient for an MVP-scale backend without adding a new dependency.
    //
    // KNOWN LIMITATION behind Azure Container Apps (or any reverse proxy):
    // Connection.RemoteIpAddress is the immediate TCP peer, which behind a
    // proxy is the proxy's own address, not the original client's. No
    // ForwardedHeaders handling has been added to fix this (see
    // docs/adr/004-production-hosting.md for why, and why this is tracked
    // as an open risk to verify in staging rather than treated as solved).
    rateLimiterOptions.AddPolicy(RateLimiterPolicyName, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { type = "about:blank", title = "rate_limited", status = StatusCodes.Status429TooManyRequests },
            cancellationToken);
    };
});

var app = builder.Build();

// Never leak exception details for genuinely unhandled errors — always a
// generic 500 with no message, stack trace, or configuration/secret value.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new { type = "about:blank", title = "internal_error", status = 500 });
}));

if (app.Environment.IsProduction())
{
    // HSTS only in Production — enforcing it during local/dev HTTP testing
    // would just be friction, not a real security gain there.
    app.UseHsts();
}

var hostingOptions = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value;

if (!hostingOptions.BehindTlsTerminatingProxy)
{
    // Unchanged local/dev + non-proxied-production behavior: this app
    // terminates the HTTP->HTTPS redirect itself.
    app.UseHttpsRedirection();
}

// else: Hosting:BehindTlsTerminatingProxy=true means a TLS-terminating
// reverse proxy (Azure Container Apps ingress) already enforced HTTPS
// before this request arrived over plain HTTP. Redirecting here would
// create an infinite redirect loop, since every request Kestrel sees in
// that topology looks like plain HTTP.

// Deliberately no app.UseForwardedHeaders(...): trusting X-Forwarded-* headers
// is only safe once the actual reverse proxy topology is known. Until a
// hosting decision is made, the safe default is to trust nothing — see the
// rate limiter partition-key comment above and docs/adr/004-production-hosting.md
// for the known consequence of this choice.

app.UseRateLimiter();

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals(OAuthExchangePath, StringComparison.OrdinalIgnoreCase) &&
        context.Request.ContentLength is > MaxOAuthExchangeRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { type = "about:blank", title = "payload_too_large", status = 413 });
        return;
    }

    await next();
});

// Deliberately returns nothing beyond a fixed status literal — no
// configuration, options, or secret value is ever included in the response.
// Exempt from rate limiting (no .RequireRateLimiting call).
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost(OAuthExchangePath, async (
    HttpContext httpContext,
    IGitHubTokenExchangeService exchangeService,
    CancellationToken cancellationToken) =>
{
    // Applies to every response from this endpoint, success or failure —
    // access/refresh tokens must never be cached by a client or intermediary.
    httpContext.Response.Headers.CacheControl = "no-store";
    httpContext.Response.Headers.Pragma = "no-cache";

    // Read the body explicitly (rather than relying on implicit complex-type
    // binding) so a JsonException — including one from the strict
    // UnmappedMemberHandling.Disallow policy rejecting unknown fields like a
    // smuggled "clientSecret" — maps to a clean 400, not an unhandled 500.
    GitHubTokenExchangeRequest? request;
    try
    {
        request = await httpContext.Request.ReadFromJsonAsync<GitHubTokenExchangeRequest>(cancellationToken);
    }
    catch (JsonException)
    {
        return Results.Problem(type: "about:blank", title: "invalid_request", statusCode: StatusCodes.Status400BadRequest);
    }

    if (!GitHubTokenExchangeRequestValidator.IsValid(request))
    {
        return Results.Problem(
            type: "about:blank",
            title: "invalid_request",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var outcome = await exchangeService.ExchangeAsync(request!.Code!, request.CodeVerifier!, cancellationToken);

    if (outcome.IsSuccess)
    {
        return Results.Json(outcome.Success, statusCode: StatusCodes.Status200OK);
    }

    return outcome.FailureKind switch
    {
        GitHubTokenExchangeFailureKind.OAuthRejected => Results.Problem(
            type: "about:blank", title: "oauth_exchange_failed", statusCode: StatusCodes.Status400BadRequest),
        GitHubTokenExchangeFailureKind.UpstreamTimeout => Results.Problem(
            type: "about:blank", title: "upstream_timeout", statusCode: StatusCodes.Status504GatewayTimeout),
        _ => Results.Problem(
            type: "about:blank", title: "upstream_error", statusCode: StatusCodes.Status502BadGateway)
    };
})
.RequireRateLimiting(RateLimiterPolicyName);

app.Run();

// Exposed for WebApplicationFactory<Program> in RepoPulse.AuthApi.Tests.
public partial class Program { }
