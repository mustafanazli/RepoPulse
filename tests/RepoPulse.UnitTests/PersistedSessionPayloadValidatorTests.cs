using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class PersistedSessionPayloadValidatorTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static PersistedSessionPayload CreatePayload(
        int version = PersistedSessionPayload.CurrentVersion,
        string? accessToken = "FAKE-ACCESS-TOKEN-TEST-ONLY",
        string? refreshToken = "FAKE-REFRESH-TOKEN-TEST-ONLY",
        string? login = "octocat",
        string? avatarUrl = "https://example.com/avatar.png",
        DateTimeOffset? accessTokenExpiresAtUtc = null) =>
        new(version, accessToken, refreshToken, login, avatarUrl, accessTokenExpiresAtUtc);

    [Fact]
    public void Serialize_ThenTryParse_RoundTripsAllFields()
    {
        var payload = CreatePayload(accessTokenExpiresAtUtc: UtcNow.AddHours(1));

        var json = PersistedSessionPayloadValidator.Serialize(payload);
        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.True(parsed);
        Assert.Null(reason);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void Serialize_ProducesExactlyOneVersionField_NoTypeMetadata()
    {
        var payload = CreatePayload();

        var json = PersistedSessionPayloadValidator.Serialize(payload);

        Assert.Contains("\"Version\":1", json);
        Assert.DoesNotContain("$type", json);
    }

    [Fact]
    public void TryParse_NullRefreshToken_StillSucceeds()
    {
        var payload = CreatePayload(refreshToken: null);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.True(parsed);
        Assert.Null(reason);
        Assert.Null(result!.RefreshToken);
    }

    [Fact]
    public void TryParse_NoExpiration_IsNeverConsideredExpired()
    {
        var payload = CreatePayload(accessTokenExpiresAtUtc: null);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow.AddYears(10), out _, out var reason);

        Assert.True(parsed);
        Assert.Null(reason);
    }

    [Fact]
    public void TryParse_ExpirationInFuture_Succeeds()
    {
        var payload = CreatePayload(accessTokenExpiresAtUtc: UtcNow.AddMinutes(30));
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.True(parsed);
        Assert.Null(reason);
    }

    [Fact]
    public void TryParse_ExpiredWellPastClockSkewTolerance_IsRejected()
    {
        var payload = CreatePayload(accessTokenExpiresAtUtc: UtcNow.AddHours(-1));
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.AccessTokenExpired, reason);
    }

    [Fact]
    public void TryParse_JustWithinClockSkewTolerance_IsAccepted()
    {
        var expiresAt = UtcNow - PersistedSessionPayloadValidator.ClockSkewTolerance + TimeSpan.FromSeconds(1);
        var payload = CreatePayload(accessTokenExpiresAtUtc: expiresAt);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.True(parsed);
        Assert.Null(reason);
    }

    [Fact]
    public void TryParse_JustBeyondClockSkewTolerance_IsRejected()
    {
        var expiresAt = UtcNow - PersistedSessionPayloadValidator.ClockSkewTolerance - TimeSpan.FromSeconds(1);
        var payload = CreatePayload(accessTokenExpiresAtUtc: expiresAt);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.False(parsed);
        Assert.Equal(PersistedSessionRejectionReason.AccessTokenExpired, reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ \"Version\": 1, ")]
    [InlineData("[1,2,3]")]
    [InlineData("\"just a string\"")]
    public void TryParse_MalformedJson_IsRejected(string malformedJson)
    {
        var parsed = PersistedSessionPayloadValidator.TryParse(malformedJson, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.MalformedJson, reason);
    }

    [Fact]
    public void TryParse_NullJson_IsRejectedAsMalformed()
    {
        var parsed = PersistedSessionPayloadValidator.TryParse(null, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.MalformedJson, reason);
    }

    [Fact]
    public void TryParse_UnsupportedVersion_IsRejected()
    {
        var payload = CreatePayload(version: PersistedSessionPayload.CurrentVersion + 1);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.UnsupportedVersion, reason);
    }

    [Fact]
    public void TryParse_OlderVersion_IsAlsoRejected_NoMigrationAttempted()
    {
        var payload = CreatePayload(version: 0);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.False(parsed);
        Assert.Equal(PersistedSessionRejectionReason.UnsupportedVersion, reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_MissingAccessToken_IsRejected(string? accessToken)
    {
        var payload = CreatePayload(accessToken: accessToken);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.MissingAccessToken, reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_MissingLogin_IsRejected(string? login)
    {
        var payload = CreatePayload(login: login);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.MissingLogin, reason);
    }

    [Theory]
    [InlineData("ftp://example.com/avatar.png")]
    [InlineData("not-a-url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path.png")]
    public void TryParse_InvalidAvatarUrl_IsRejected(string avatarUrl)
    {
        var payload = CreatePayload(avatarUrl: avatarUrl);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.InvalidAvatarUrl, reason);
    }

    [Fact]
    public void TryParse_NullAvatarUrl_IsAccepted()
    {
        var payload = CreatePayload(avatarUrl: null);
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.True(parsed);
        Assert.Null(reason);
    }

    [Fact]
    public void TryParse_OverlyLongAccessToken_IsRejected()
    {
        var payload = CreatePayload(accessToken: new string('a', 5000));
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out var result, out var reason);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(PersistedSessionRejectionReason.FieldTooLong, reason);
    }

    [Fact]
    public void TryParse_OverlyLongLogin_IsRejected()
    {
        var payload = CreatePayload(login: new string('b', 5000));
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.False(parsed);
        Assert.Equal(PersistedSessionRejectionReason.FieldTooLong, reason);
    }

    [Fact]
    public void TryParse_OverlyLongAvatarUrl_IsRejected()
    {
        var payload = CreatePayload(avatarUrl: "https://example.com/" + new string('c', 5000) + ".png");
        var json = PersistedSessionPayloadValidator.Serialize(payload);

        var parsed = PersistedSessionPayloadValidator.TryParse(json, UtcNow, out _, out var reason);

        Assert.False(parsed);
        Assert.Equal(PersistedSessionRejectionReason.FieldTooLong, reason);
    }
}
