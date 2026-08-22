using System.Security.Cryptography;
using System.Text;

namespace RepoPulse.Core.Authentication;

// RFC 7636 PKCE helpers. code_verifier/state use 32 random bytes, base64url
// (no padding) encoded -> 43 characters, within the RFC's 43-128 char range
// and its unreserved [A-Za-z0-9-._~] charset.
public static class PkceGenerator
{
    private const int RandomByteLength = 32;

    public static string CreateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomByteLength));

    public static string CreateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomByteLength));

    public static string CreateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
