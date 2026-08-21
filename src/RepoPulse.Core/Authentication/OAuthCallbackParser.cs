using System.Text;

namespace RepoPulse.Core.Authentication;

// Parses the redirect URI GitHub sends back to the app after OAuth authorization
// (repopulse://oauth/callback?...). Pure logic, no MAUI/Android dependency, so it
// stays testable and shareable via a ProjectReference from both the app and tests.
public static class OAuthCallbackParser
{
    public static OAuthCallbackResult Parse(Uri? uri)
    {
        if (uri is null || !IsRecognizedCallbackUri(uri))
        {
            return OAuthCallbackResult.Invalid();
        }

        if (!TryParseQuery(uri.Query, out var query))
        {
            // Malformed percent-encoding: reject rather than guess.
            return OAuthCallbackResult.Invalid();
        }

        query.TryGetValue("code", out var code);
        query.TryGetValue("state", out var state);
        query.TryGetValue("error", out var error);
        query.TryGetValue("error_description", out var errorDescription);

        if (!string.IsNullOrEmpty(error))
        {
            return string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                ? OAuthCallbackResult.Cancelled(error, errorDescription)
                : OAuthCallbackResult.Invalid(error, errorDescription);
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return OAuthCallbackResult.Invalid();
        }

        return OAuthCallbackResult.Success(code, state);
    }

    private static bool IsRecognizedCallbackUri(Uri uri) =>
        string.Equals(uri.Scheme, OAuthConstants.CallbackScheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, OAuthConstants.CallbackHost, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.AbsolutePath, OAuthConstants.CallbackPath, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseQuery(string query, out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var rawKey = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var rawValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;

            if (!TryDecodeFormUrlEncodedValue(rawKey, out var key) ||
                !TryDecodeFormUrlEncodedValue(rawValue, out var value))
            {
                return false;
            }

            result[key] = value;
        }

        return true;
    }

    // application/x-www-form-urlencoded decoding: '+' means space, '%XX' is a
    // percent-encoded byte. Never throws — an incomplete/invalid '%' escape or a
    // stray non-ASCII character makes the whole value fail to decode instead.
    //
    // Internal (not private): System.Uri itself repairs a malformed '%' escape
    // into a well-formed one (e.g. "abc%2" -> query "abc%252") before this code
    // ever sees it, so Parse(Uri) can no longer observe truly malformed input.
    // This method's own malformed-input contract is still tested directly.
    internal static bool TryDecodeFormUrlEncodedValue(string value, out string decoded)
    {
        var bytes = new List<byte>(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '+')
            {
                bytes.Add((byte)' ');
                continue;
            }

            if (c == '%')
            {
                if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2]))
                {
                    decoded = string.Empty;
                    return false;
                }

                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            if (c > 127)
            {
                decoded = string.Empty;
                return false;
            }

            bytes.Add((byte)c);
        }

        decoded = Encoding.UTF8.GetString(bytes.ToArray());
        return true;
    }
}
