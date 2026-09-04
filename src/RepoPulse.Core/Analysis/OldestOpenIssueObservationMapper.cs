using RepoPulse.Core.Repositories;
using RepoPulse.Core.Scoring;

namespace RepoPulse.Core.Analysis;

// RP-022: the adaptation seam between RP-020's API result
// (GitHubOldestOpenIssueResult) and RP-021's scoring input
// (OldestOpenIssueObservation).
//
// This is the layer both of those types deliberately left unwritten.
// GitHubOldestOpenIssueResult knows about GraphQL outcomes and failure
// kinds; OldestOpenIssueObservation knows only about the three states
// scoring cares about; neither references the other, and neither should.
// This mapper is the single place allowed to decide which API outcomes
// become which observation, exactly as OldestOpenIssueObservation's own doc
// comment anticipated.
//
// SCOPE: this turn is the conversion ONLY. This type produces no score, and
// the combined Bakim (maintenance) sub-score, the weighted overall Health
// Score, and every other Faz 2 scorer remain out of scope. It makes no
// GitHub/GraphQL/REST request of its own, adds no endpoint, and does not
// depend on IGitHubApiClient or HttpClient — it is a pure, synchronous
// function over a result object the caller already holds.
//
// CONVERSION TABLE:
//   Success + HasOpenIssues + CreatedAtUtc   -> Found(CreatedAtUtc)
//   Success + !HasOpenIssues                 -> NoOpenIssues()
//   Failure (RepositoryUnavailable)          -> NoData()
//   Failure (Unauthorized)                   -> NoData()
//   Failure (RateLimited)                    -> NoData()
//   Failure (NetworkError)                   -> NoData()
//   Failure (Unexpected)                     -> NoData()
//
// EVERY FAILURE COLLAPSES TO NoData, BY DESIGN. The mapper does not switch
// on FailureKind at all; it asks only whether the query could be answered.
// Two consequences follow, both intentional:
//   - The failure reason never reaches the scoring layer. Carrying it there
//     would invite a future "score a rate-limit differently from a network
//     error" rule, which would be scoring the health of our own API session
//     rather than the health of the repository. It also keeps raw error
//     text, response bodies, URLs, headers and tokens structurally unable
//     to travel into scoring — the observation has nowhere to put them.
//   - A failure kind added to the enum later automatically becomes NoData
//     rather than silently falling through to some other branch. There is
//     no per-kind list here to forget to update.
//
// AN API FAILURE IS NOT A REPOSITORY FACT. NoData means "we cannot say",
// and RP-021 scores it as Value=null so a future aggregation can exclude
// the component. A failure must never become NoOpenIssues: that is a
// positive, data-verified claim about the repository ("GitHub confirmed the
// backlog is empty") which scores 100/Clear. Turning an unanswered query
// into a perfect score is precisely the conflation the three-state
// observation model exists to prevent.
//
// WHY RepositoryUnavailable IS NOT NotApplicable. It is tempting to read
// "the repository is unavailable" as "this repository should not be
// scored", but the two are unrelated. RepositoryUnavailable is a GraphQL
// data.repository == null shape that means EITHER the repository does not
// exist OR our token simply cannot see it — the result type makes no claim
// about which, and neither can this mapper. NotApplicable, by contrast, is
// a statement about a repository we CAN see: it is archived or a fork, so
// no maintenance expectation applies. That decision needs the archived/fork
// flags, which this mapper never receives, and it belongs to the scorer,
// which already takes isArchived/isFork and returns NotApplicable itself.
// Mapping an access failure to NotApplicable would let a missing permission
// masquerade as a deliberate policy decision about a repository we never
// actually observed.
//
// TIME: no system clock read of any kind. The scorer takes nowUtc from its
// caller, and this mapper does not need "now" at all — it neither computes
// an age nor timestamps anything. The Found timestamp passes through
// untouched as a DateTimeOffset (never via string formatting/parsing, never
// through a local-time conversion), and OldestOpenIssueObservation.Found
// applies the UTC normalization that is its invariant to enforce, not this
// mapper's to duplicate.
public static class OldestOpenIssueObservationMapper
{
    public static OldestOpenIssueObservation Map(GitHubOldestOpenIssueResult result)
    {
        // A null result is a caller/programming error, not a data outcome.
        // Absorbing it into NoData would let a broken call site look like a
        // repository whose issue query merely failed.
        ArgumentNullException.ThrowIfNull(result);

        // Asked first and without inspecting FailureKind: any unsuccessful
        // outcome, present or future, is data we cannot trust.
        if (!result.IsSuccess)
        {
            return OldestOpenIssueObservation.NoData();
        }

        // A successful query that positively confirmed an empty backlog.
        if (!result.HasOpenIssues)
        {
            return OldestOpenIssueObservation.NoOpenIssues();
        }

        // Unreachable through GitHubOldestOpenIssueResult's public surface:
        // its only route to IsSuccess && HasOpenIssues is Success(), which
        // takes a non-nullable DateTimeOffset. Kept as a fail-fast because
        // the two alternatives are both worse — inventing a fallback date
        // would fabricate a repository fact out of nothing, and quietly
        // returning NoOpenIssues or NoData would score a broken invariant
        // as a real observation and hide the defect. A violated invariant
        // is a bug in the producing layer and should surface as one.
        if (result.CreatedAtUtc is not { } createdAtUtc)
        {
            throw new InvalidOperationException(
                "A successful oldest-open-issue result reporting an open issue must carry a creation timestamp.");
        }

        return OldestOpenIssueObservation.Found(createdAtUtc);
    }
}
