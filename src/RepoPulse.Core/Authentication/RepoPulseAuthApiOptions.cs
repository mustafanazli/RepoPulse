namespace RepoPulse.Core.Authentication;

// DEVELOPMENT-ONLY backend base address, wired through DI (see
// MauiProgram.cs) rather than read from any user-facing setting — the app
// never lets a user type in a backend URL. The production hosting address
// is a separate, not-yet-decided task (see plan RP-004 hosting note); this
// constant must be replaced with an environment-appropriate configuration
// source before shipping — MauiProgram.cs raises a compiler #warning on
// every non-DEBUG build until that happens, specifically because neither
// "localhost" nor the Android-emulator alias "10.0.2.2" resolve to
// anything on a real device or in production.
//
// MauiProgram.cs.ResolveAuthApiBaseAddress() overrides this to
// "https://10.0.2.2:7082" specifically for Android DEBUG builds, since the
// emulator cannot reach the host machine via "localhost" — 10.0.2.2 is its
// documented alias for that. Every other debug target keeps using the
// value below directly.
public static class RepoPulseAuthApiOptions
{
    // Live Azure Container Apps STAGING backend — not production. Deployed
    // via infra/azure/app.bicep (Phase B); see
    // docs/deployment/azure-staging-runbook.md. Public, non-secret address
    // (no client_secret, token, or credential of any kind is embedded here
    // or reachable from it without the backend's own Key Vault-backed
    // secret). A known, documented, unresolved risk still applies to this
    // address before it could ever be treated as production: the rate
    // limiter's client-IP partitioning behind Container Apps ingress has
    // not yet been verified end-to-end (see docs/adr/004-production-hosting.md,
    // "PRODUCTION DEPLOYMENT BLOCKER") — do not repurpose this address as a
    // production endpoint without first completing that staging test.
    public const string StagingBaseAddress = "https://ca-repopulse-authapi-staging.orangefield-f1a16f03.polandcentral.azurecontainerapps.io";

    // DEVELOPMENT-ONLY backend base address, wired through DI (see
    // MauiProgram.cs) rather than read from any user-facing setting — the app
    // never lets a user type in a backend URL. The production hosting address
    // is a separate, not-yet-decided task (see plan RP-004 hosting note); this
    // constant must be replaced with an environment-appropriate configuration
    // source before shipping — MauiProgram.cs raises a compiler #warning on
    // every non-DEBUG build until that happens, specifically because neither
    // "localhost" nor the Android-emulator alias "10.0.2.2" resolve to
    // anything on a real device or in production.
    //
    // MauiProgram.cs.ResolveAuthApiBaseAddress() overrides this to
    // "https://10.0.2.2:7082" specifically for Android DEBUG builds when
    // local backend testing is explicitly requested (see
    // MauiProgram.UseLocalDevelopmentAuthApi) — otherwise Android DEBUG
    // defaults to StagingBaseAddress above. Every other debug target keeps
    // using the value below directly.
    public const string DevelopmentBaseAddress = "https://localhost:7082";
}
