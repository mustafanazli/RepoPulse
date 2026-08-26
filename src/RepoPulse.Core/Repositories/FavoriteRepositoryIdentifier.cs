namespace RepoPulse.Core.Repositories;

// RP-012's single source of truth for what makes two (owner, name) pairs the
// "same" favorite — mirrors RP-011's OrdinalIgnoreCase FullName identity
// (RepositoryListItemSynchronizer.AreSameIdentity) so a repository that is
// simultaneously a favorite and visible in the live list is never treated as
// two different identities by the two features. ToLowerInvariant() (not
// ToLower(), which is culture-sensitive) is used for the persisted key so a
// device's current culture can never change which row an existing favorite
// maps to — GitHub owner/repository names are ASCII-only, so
// ToLowerInvariant and OrdinalIgnoreCase agree in every case.
public static class FavoriteRepositoryIdentifier
{
    public readonly record struct NormalizedIdentity(string Owner, string Name, string NormalizedFullName);

    public static bool TryNormalize(string? owner, string? name, out NormalizedIdentity identity)
    {
        var trimmedOwner = owner?.Trim();
        var trimmedName = name?.Trim();

        if (string.IsNullOrEmpty(trimmedOwner) || string.IsNullOrEmpty(trimmedName))
        {
            identity = default;
            return false;
        }

        identity = new NormalizedIdentity(trimmedOwner, trimmedName, NormalizeFullName($"{trimmedOwner}/{trimmedName}"));
        return true;
    }

    // Shared by callers that already have a GitHub-cased "owner/name" string
    // (e.g. GitHubRepository.FullName / RepositoryListItem.FullName) so both
    // paths produce byte-identical keys for the same repository.
    public static string NormalizeFullName(string fullName) => fullName.ToLowerInvariant();

    // Account-isolation identity (fix for the cross-account favorite leak):
    // every favorite row is scoped to the GitHub login that added it, using
    // the exact same Trim + ToLowerInvariant technique as repository
    // identity above — never a token or a hash of one. GitHub logins are
    // ASCII-only, so this is culture-independent for the same reason
    // NormalizeFullName is.
    public static bool TryNormalizeAccountLogin(string? accountLogin, out string normalizedAccountLogin)
    {
        var trimmed = accountLogin?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            normalizedAccountLogin = string.Empty;
            return false;
        }

        normalizedAccountLogin = trimmed.ToLowerInvariant();
        return true;
    }
}
