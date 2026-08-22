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
    public const string DevelopmentBaseAddress = "https://localhost:7082";
}
