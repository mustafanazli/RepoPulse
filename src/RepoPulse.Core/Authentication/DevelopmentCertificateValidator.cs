using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RepoPulse.Core.Authentication;

// DEBUG-only, narrowly-scoped acceptance of the local ASP.NET Core HTTPS
// development certificate when reached from an Android emulator via
// 10.0.2.2 (the emulator's alias for the host machine's localhost) or from
// localhost directly. This is NOT a blanket "accept any certificate"
// validator: every check below must pass, and it must only ever be wired
// into the AuthApi HttpClient, only inside #if DEBUG (see MauiProgram.cs) —
// GitHubApiClient and every Release build use ordinary platform TLS
// validation. Kept here, pure and dependency-free beyond BCL types, so it
// stays unit-testable without any MAUI/Android/network dependency.
public static class DevelopmentCertificateValidator
{
    private static readonly string[] AllowedHosts = { "10.0.2.2", "localhost", "127.0.0.1" };
    private const string ExpectedSubject = "CN=localhost";
    private const string ExpectedIssuer = "CN=localhost";

    // Single source of truth for "is this a local-development host" so the
    // AuthApi HttpClient wiring in MauiProgram.cs can decide whether to
    // attach this validator at all, without duplicating the host list. A
    // live backend (e.g. the Azure staging address) is never one of these
    // hosts, so it always falls through to ordinary platform TLS validation
    // — see MauiProgram.CreateAuthApiHttpClient().
    public static bool IsLocalDevelopmentHost(string? host) =>
        host is not null && AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    public static bool ShouldAccept(HttpRequestMessage? request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        var host = request?.RequestUri?.Host;
        return ShouldAccept(host, certificate);
    }

    public static bool ShouldAccept(string? requestHost, X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            return false;
        }

        if (requestHost is null || !AllowedHosts.Contains(requestHost, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(certificate.Subject, ExpectedSubject, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(certificate.Issuer, ExpectedIssuer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // X509Certificate2.NotBefore/NotAfter are DateTime in local time by
        // .NET convention — normalize to UTC before comparing.
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < certificate.NotBefore.ToUniversalTime() || nowUtc > certificate.NotAfter.ToUniversalTime())
        {
            return false;
        }

        return true;
    }
}
