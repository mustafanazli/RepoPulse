namespace RepoPulse.Core.Repositories;

// The ONLY place user-typed text is turned into GitHub API path segments
// (see GitHubApiClient.GetRepositoryAsync) — so it must fail closed on
// anything ambiguous or unexpected rather than best-effort guess at intent.
// Accepts exactly two forms:
//   - "owner/repository"
//   - "https://github.com/owner/repository" (or http://)
// Rejects: empty/whitespace input, non-GitHub hosts, extra path segments,
// any query string or fragment, and owner/repository segments containing
// characters outside GitHub's own safe identifier charset.
public static class RepositoryIdentifierParser
{
    private const int MaxOwnerLength = 39;
    private const int MaxNameLength = 100;

    public static RepositoryIdentifierParseResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return RepositoryIdentifierParseResult.Failure("Bir repository adı girin.");
        }

        var trimmed = input.Trim();

        string ownerCandidate;
        string nameCandidate;

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseGitHubUrl(trimmed, out ownerCandidate!, out nameCandidate!))
            {
                return RepositoryIdentifierParseResult.Failure("Geçerli bir GitHub repository adresi girin (ör. owner/repository).");
            }
        }
        else
        {
            var parts = trimmed.Split('/');
            if (parts.Length != 2)
            {
                return RepositoryIdentifierParseResult.Failure("Repository adını \"owner/repository\" biçiminde girin.");
            }

            ownerCandidate = parts[0];
            nameCandidate = parts[1];
        }

        if (nameCandidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            nameCandidate = nameCandidate[..^4];
        }

        if (!IsValidSegment(ownerCandidate, MaxOwnerLength) || !IsValidSegment(nameCandidate, MaxNameLength))
        {
            return RepositoryIdentifierParseResult.Failure("Repository adı geçersiz karakterler içeriyor.");
        }

        return RepositoryIdentifierParseResult.Success(new RepositoryIdentifier(ownerCandidate, nameCandidate));
    }

    private static bool TryParseGitHubUrl(string input, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Only a bare owner/repository address is accepted — never one with
        // extra query parameters or a fragment attached.
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        owner = Uri.UnescapeDataString(segments[0]);
        name = Uri.UnescapeDataString(segments[1]);
        return true;
    }

    private static bool IsValidSegment(string value, int maxLength)
    {
        if (value.Length == 0 || value.Length > maxLength || value is "." or "..")
        {
            return false;
        }

        foreach (var c in value)
        {
            var isAsciiLetterOrDigit = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (!isAsciiLetterOrDigit && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
