using System.Net;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RepoPulse.AuthApi.Tests;

// Covers the Hosting:BehindTlsTerminatingProxy switch introduced for Azure
// Container Apps (see docs/adr/004-production-hosting.md). Never contacts
// GitHub or Azure — only exercises local HTTP redirect behavior.
public class HostingOptionsTests
{
    [Fact]
    public async Task ProxyModeOff_HttpRequest_RedirectsToHttps()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureServices(services =>
            {
                // Without a known HTTPS port, UseHttpsRedirection silently
                // no-ops instead of redirecting — configure one explicitly
                // so this test actually exercises the redirect.
                services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health");

        Assert.True(
            response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect or HttpStatusCode.Redirect,
            $"expected a redirect status code, got {response.StatusCode}");
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task ProxyModeOn_HttpRequest_ReturnsHealthWithoutRedirectLoop()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = TestConfiguration.Valid();
                values["Hosting:BehindTlsTerminatingProxy"] = "true";
                config.AddInMemoryCollection(values);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ClientSuppliedHostingFields_DoNotOverrideConfiguredProxyMode()
    {
        // Proxy mode is configured OFF on the server; a client cannot flip
        // it to ON (and thus skip the redirect) via headers or a body field
        // of the same name — the option is bound once from configuration at
        // startup and never re-read per request.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestConfiguration.Valid()));
            builder.ConfigureServices(services =>
            {
                services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("Hosting:BehindTlsTerminatingProxy", "true");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect or HttpStatusCode.Redirect,
            $"expected the server-configured redirect to still apply, got {response.StatusCode}");
    }
}
