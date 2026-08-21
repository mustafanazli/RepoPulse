using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class OAuthCallbackParserTests
{
    [Fact]
    public void Parse_ValidCodeAndState_ReturnsSuccess()
    {
        var uri = new Uri("repopulse://oauth/callback?code=abc123&state=xyz789");

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Success, result.Outcome);
        Assert.Equal("abc123", result.Code);
        Assert.Equal("xyz789", result.State);
    }

    [Fact]
    public void Parse_AccessDeniedError_ReturnsCancelled()
    {
        var uri = new Uri("repopulse://oauth/callback?error=access_denied&error_description=The+user+cancelled");

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Cancelled, result.Outcome);
        Assert.Equal("The user cancelled", result.ErrorDescription);
    }

    [Fact]
    public void Parse_PercentEncodedValues_DecodeCorrectly()
    {
        // "abc def" and "xyz/789" percent-encoded.
        var uri = new Uri("repopulse://oauth/callback?code=abc%20def&state=xyz%2F789");

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Success, result.Outcome);
        Assert.Equal("abc def", result.Code);
        Assert.Equal("xyz/789", result.State);
    }

    // System.Uri repairs a malformed '%' escape (e.g. "abc%2" -> query "abc%252")
    // before Parse(Uri) ever sees it, so malformed input can no longer reach the
    // decoder through the public API. These exercise the internal decode routine
    // directly to verify its own malformed-input contract still holds.
    [Theory]
    [InlineData("abc%2")]
    [InlineData("abc%")]
    [InlineData("abc%zz")]
    public void TryDecodeFormUrlEncodedValue_MalformedPercentEncoding_ReturnsFalse(string rawValue)
    {
        var succeeded = OAuthCallbackParser.TryDecodeFormUrlEncodedValue(rawValue, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void Parse_MalformedPercentEncodingViaUri_DoesNotThrowAndIsHandledSafely()
    {
        // System.Uri normalizes this to a well-formed, literal round-trip rather
        // than crashing or silently dropping data — verifies requirement #8
        // (unexpected/malformed data must never crash the parser).
        var uri = new Uri("repopulse://oauth/callback?code=abc%2&state=xyz789");

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Success, result.Outcome);
        Assert.Equal("abc%2", result.Code);
    }

    [Fact]
    public void Parse_MissingState_ReturnsInvalid()
    {
        var uri = new Uri("repopulse://oauth/callback?code=abc123");

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Parse_MissingCodeAndError_ReturnsInvalid()
    {
        var uri = new Uri("repopulse://oauth/callback?state=xyz789");

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Invalid, result.Outcome);
    }

    [Theory]
    [InlineData("wrongscheme://oauth/callback?code=abc123&state=xyz789")]
    [InlineData("repopulse://wronghost/callback?code=abc123&state=xyz789")]
    [InlineData("repopulse://oauth/wrongpath?code=abc123&state=xyz789")]
    public void Parse_UnexpectedSchemeHostOrPath_ReturnsInvalid(string rawUri)
    {
        var uri = new Uri(rawUri);

        var result = OAuthCallbackParser.Parse(uri);

        Assert.Equal(OAuthCallbackOutcome.Invalid, result.Outcome);
    }
}
