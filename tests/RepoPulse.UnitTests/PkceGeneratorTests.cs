using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class PkceGeneratorTests
{
    // RFC 7636 unreserved charset: [A-Z] [a-z] [0-9] "-" "." "_" "~"
    private static readonly Regex AllowedCharset = new("^[A-Za-z0-9\\-._~]+$", RegexOptions.Compiled);

    [Fact]
    public void CreateCodeVerifier_HasValidRfc7636FormatAndLength()
    {
        var verifier = PkceGenerator.CreateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches(AllowedCharset, verifier);
    }

    [Fact]
    public void CreateCodeVerifier_IsRandomAcrossCalls()
    {
        var first = PkceGenerator.CreateCodeVerifier();
        var second = PkceGenerator.CreateCodeVerifier();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateState_HasValidFormatAndIsRandom()
    {
        var first = PkceGenerator.CreateState();
        var second = PkceGenerator.CreateState();

        Assert.InRange(first.Length, 43, 128);
        Assert.Matches(AllowedCharset, first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateCodeChallenge_MatchesRfc7636AppendixBTestVector()
    {
        // https://www.rfc-editor.org/rfc/rfc7636#appendix-B
        const string knownVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = PkceGenerator.CreateCodeChallenge(knownVerifier);

        Assert.Equal(expectedChallenge, challenge);
    }

    [Fact]
    public void CreateCodeChallenge_IsSha256Base64UrlOfVerifier()
    {
        var verifier = PkceGenerator.CreateCodeVerifier();
        var expectedHash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier));
        var expectedChallenge = Convert.ToBase64String(expectedHash).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var challenge = PkceGenerator.CreateCodeChallenge(verifier);

        Assert.Equal(expectedChallenge, challenge);
        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
    }
}
