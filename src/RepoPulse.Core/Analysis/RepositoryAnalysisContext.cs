namespace RepoPulse.Core.Analysis;

// RP-023: the minimum repository context one oldest-open-issue analysis run
// needs — the identity to query and the two flags
// OldestOpenIssueAgeScorer's applicability policy reads.
//
// SCOPE: this represents what THIS analysis slice needs today. It is not,
// and does not claim to be, a complete description of a repository or of
// every future analysis signal's inputs. DefaultBranch is deliberately
// absent: nothing in this slice reads it, and adding a field "because a
// later signal might want it" would ship an unvalidated, untested value
// that no test could meaningfully constrain. A later signal that needs
// more adds it then, with its own invariants.
//
// CARRIES NO TOKEN. The access token is a transient parameter of
// OldestOpenIssueAnalyzer.AnalyzeAsync and nothing else; putting it in a
// context object would give it a lifetime, make it copyable, and make it
// visible to anything holding the context. This type also carries no
// CancellationToken, no API client, no API result and no score.
//
// IDENTITY IS VALIDATED EVEN FOR ARCHIVED/FORK REPOSITORIES. The analyzer
// short-circuits archived and fork repositories before it would use
// Owner/Name, so a malformed identity would otherwise slip through
// unnoticed on exactly that path and only surface later, when the same
// context was reused for a signal that does make a request. Validating at
// construction means applicability can never mask a bad identity: an
// invalid owner/name fails here, whatever the flags say.
public sealed record RepositoryAnalysisContext
{
    public string Owner { get; }

    public string Name { get; }

    public bool IsArchived { get; }

    public bool IsFork { get; }

    private RepositoryAnalysisContext(string owner, string name, bool isArchived, bool isFork)
    {
        Owner = owner;
        Name = name;
        IsArchived = isArchived;
        IsFork = isFork;
    }

    // The single construction route: private constructor + one narrow
    // factory, no public/init setters and no generic Create, matching the
    // RP-020/RP-021/RP-022 pattern. Because every property is get-only and
    // the generated copy constructor of a sealed record is private, `with`
    // cannot rewrite an identity or a flag from outside this type either.
    //
    // Values are stored EXACTLY as supplied — never trimmed, lower-cased or
    // otherwise "helpfully" rewritten. Silently altering an identifier
    // would mean the analyzer queried a repository the caller did not ask
    // for, and a result whose provenance no longer matches its request.
    //
    // GitHub's own identifier rules (allowed characters, maximum length)
    // are deliberately NOT duplicated here. GitHubApiClient already
    // enforces them for the endpoint it calls, and a second, drifting copy
    // of that policy in the analysis layer would be worse than none: this
    // factory rejects only what is structurally meaningless as an
    // identity — null, empty, or whitespace.
    public static RepositoryAnalysisContext Create(string owner, string name, bool isArchived, bool isFork)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("Repository owner must not be empty or whitespace.", nameof(owner));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Repository name must not be empty or whitespace.", nameof(name));
        }

        return new RepositoryAnalysisContext(owner, name, isArchived, isFork);
    }
}
