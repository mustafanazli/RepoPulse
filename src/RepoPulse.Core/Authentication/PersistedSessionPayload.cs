namespace RepoPulse.Core.Authentication;

// The exact shape written to SecureStorage under a single key (RP-008).
// Plain POCO/record — no type metadata, no polymorphic serialization.
// Fields are intentionally nullable even where UserSession's are not, so a
// corrupted/incomplete stored payload deserializes without throwing and can
// instead be rejected by PersistedSessionPayloadValidator with a specific
// reason. CurrentVersion must be bumped whenever a field is added, removed,
// or reinterpreted; PersistedSessionPayloadValidator rejects any other
// version rather than attempting a migration.
public sealed record PersistedSessionPayload(
    int Version,
    string? AccessToken,
    string? RefreshToken,
    string? Login,
    string? AvatarUrl,
    DateTimeOffset? AccessTokenExpiresAtUtc)
{
    public const int CurrentVersion = 1;
}
