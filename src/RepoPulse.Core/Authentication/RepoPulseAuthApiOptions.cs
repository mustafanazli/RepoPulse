namespace RepoPulse.Core.Authentication;

// DEVELOPMENT-ONLY backend base address, wired through DI (see
// MauiProgram.cs) rather than read from any user-facing setting — the app
// never lets a user type in a backend URL. The production hosting address
// is a separate, not-yet-decided task (see plan RP-004 hosting note); this
// constant must be replaced with an environment-appropriate configuration
// source before shipping, not hardcoded for release builds.
public static class RepoPulseAuthApiOptions
{
    public const string DevelopmentBaseAddress = "https://localhost:7082";
}
