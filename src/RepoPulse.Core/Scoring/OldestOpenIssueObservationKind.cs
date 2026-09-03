namespace RepoPulse.Core.Scoring;

// RP-021: the three — and only three — canonical situations
// OldestOpenIssueAgeScorer can be asked to score. Deliberately an explicit
// three-state enum rather than a bare `DateTimeOffset?`: a nullable
// timestamp cannot distinguish "GitHub positively confirmed zero open
// issues" from "we could not obtain the data at all", and collapsing those
// two into one null is exactly the bug this type exists to make
// unrepresentable.
public enum OldestOpenIssueObservationKind
{
    // A real open issue was found and its creation timestamp is known
    // (RP-020's GetOldestOpenIssueAsync returned IsSuccess with
    // HasOpenIssues). CreatedAtUtc is non-null for this kind and only this
    // kind.
    Found,

    // The query SUCCEEDED and positively confirmed the repository has zero
    // open issues (RP-020's NoOpenIssues shape: totalCount == 0 with an
    // empty nodes array). This is verified data, not a failure, and must
    // never be scored the same way as Found or NoData.
    NoOpenIssues,

    // No trustworthy answer is available: the RP-020 query failed
    // (repository unavailable, unauthorized, rate limited, network error,
    // unexpected response), or the surrounding orchestration could not run
    // it at all. This is NOT a zero score — see OldestOpenIssueAgeScorer's
    // doc comment.
    NoData
}
