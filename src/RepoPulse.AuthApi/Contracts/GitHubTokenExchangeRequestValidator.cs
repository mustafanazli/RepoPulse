using System.Text.RegularExpressions;

namespace RepoPulse.AuthApi.Contracts;

// Values are never trimmed or otherwise modified before validation — an
// invalid value (including one with stray whitespace) is rejected, not
// "fixed up" and accepted.
public static class GitHubTokenExchangeRequestValidator
{
    private const int MaxCodeLength = 512;

    // RFC 7636 code_verifier: 43-128 chars from [A-Z a-z 0-9 - . _ ~].
    private static readonly Regex CodeVerifierPattern =
        new("^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled);

    public static bool IsValid(GitHubTokenExchangeRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(request.Code) || request.Code.Length > MaxCodeLength)
        {
            return false;
        }

        if (request.CodeVerifier is null || !CodeVerifierPattern.IsMatch(request.CodeVerifier))
        {
            return false;
        }

        return true;
    }
}
