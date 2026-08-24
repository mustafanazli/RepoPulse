using System.Text.Json;

namespace RepoPulse.Core.Authentication;

// Mirrors AuthApiExchangeResult/GitHubRepositoryResult's typed-failure-kind
// pattern: every way a stored payload can be rejected is a distinct,
// assertable value — never a raw exception message.
public enum PersistedSessionRejectionReason
{
    MalformedJson,
    UnsupportedVersion,
    MissingAccessToken,
    MissingLogin,
    InvalidAvatarUrl,
    FieldTooLong,
    AccessTokenExpired
}

// Pure, MAUI-independent parse/validate logic behind session persistence
// (RP-008) — no I/O, so "a corrupted/expired/oversized stored payload is
// rejected" is unit-testable without SecureStorage or a running MAUI host.
// Deliberately plain System.Text.Json (no JsonSerializerContext, no
// polymorphic/type-metadata options) — the payload is a single flat record.
public static class PersistedSessionPayloadValidator
{
    // Tolerates minor local-clock drift so a token isn't rejected as
    // "expired" a few seconds early because the device clock runs fast.
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(2);

    private const int MaxTokenLength = 4096;
    private const int MaxLoginLength = 256;
    private const int MaxAvatarUrlLength = 2048;

    public static string Serialize(PersistedSessionPayload payload) =>
        JsonSerializer.Serialize(payload);

    public static bool TryParse(
        string? json,
        DateTimeOffset utcNow,
        out PersistedSessionPayload? payload,
        out PersistedSessionRejectionReason? rejectionReason)
    {
        payload = null;
        rejectionReason = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            rejectionReason = PersistedSessionRejectionReason.MalformedJson;
            return false;
        }

        PersistedSessionPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PersistedSessionPayload>(json);
        }
        catch (JsonException)
        {
            rejectionReason = PersistedSessionRejectionReason.MalformedJson;
            return false;
        }

        if (parsed is null)
        {
            rejectionReason = PersistedSessionRejectionReason.MalformedJson;
            return false;
        }

        if (!Validate(parsed, utcNow, out rejectionReason))
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    public static bool Validate(
        PersistedSessionPayload payload,
        DateTimeOffset utcNow,
        out PersistedSessionRejectionReason? rejectionReason)
    {
        rejectionReason = null;

        if (payload.Version != PersistedSessionPayload.CurrentVersion)
        {
            rejectionReason = PersistedSessionRejectionReason.UnsupportedVersion;
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            rejectionReason = PersistedSessionRejectionReason.MissingAccessToken;
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.Login))
        {
            rejectionReason = PersistedSessionRejectionReason.MissingLogin;
            return false;
        }

        if (payload.AccessToken.Length > MaxTokenLength ||
            (payload.RefreshToken?.Length ?? 0) > MaxTokenLength ||
            payload.Login.Length > MaxLoginLength ||
            (payload.AvatarUrl?.Length ?? 0) > MaxAvatarUrlLength)
        {
            rejectionReason = PersistedSessionRejectionReason.FieldTooLong;
            return false;
        }

        if (!string.IsNullOrEmpty(payload.AvatarUrl) &&
            (!Uri.TryCreate(payload.AvatarUrl, UriKind.Absolute, out var avatarUri) ||
             (avatarUri.Scheme != Uri.UriSchemeHttp && avatarUri.Scheme != Uri.UriSchemeHttps)))
        {
            rejectionReason = PersistedSessionRejectionReason.InvalidAvatarUrl;
            return false;
        }

        if (payload.AccessTokenExpiresAtUtc is { } expiresAt && utcNow > expiresAt + ClockSkewTolerance)
        {
            rejectionReason = PersistedSessionRejectionReason.AccessTokenExpired;
            return false;
        }

        return true;
    }
}
