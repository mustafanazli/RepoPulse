namespace RepoPulse.AuthApi.Configuration;

// Bound once from the "Hosting" configuration section at application
// startup — this value is never read from, or overridable by, any
// per-request header or client-supplied field. See
// docs/adr/004-production-hosting.md for why this exists.
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    // false (default): unchanged local/dev behavior — the app itself
    // enforces HTTP->HTTPS redirection (see Program.cs UseHttpsRedirection).
    //
    // true: a TLS-terminating reverse proxy sits in front of this app (e.g.
    // Azure Container Apps ingress). External HTTPS is already enforced
    // before the request reaches this container over plain HTTP, so the
    // app must NOT also redirect — doing so would produce a redirect loop,
    // since every request Kestrel sees here looks like plain HTTP.
    public bool BehindTlsTerminatingProxy { get; set; }
}
