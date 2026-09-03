namespace RepoPulse.Core.Scoring;

// RP-021: the pure, transport-independent input to OldestOpenIssueAgeScorer.
//
// This type deliberately does NOT reference RepoPulse.Core.Repositories'
// GitHubOldestOpenIssueResult (RP-020) or any other API/HTTP shape: the
// scorer must stay a pure function over a domain observation, so that it
// can be tested and reasoned about without the GitHub client, and so that
// the API surface can change without dragging the scoring layer with it.
// Turning an RP-020 result into one of these three observations is the
// responsibility of a FUTURE orchestration layer, not of this type and not
// of the scorer — that layer is the single place allowed to decide which
// failure kinds map to NoData.
//
// WHY NOT `DateTimeOffset?`: a nullable timestamp has only two states, but
// there are three genuinely distinct situations (a real oldest issue, a
// verified empty backlog, and no trustworthy data). Passing a bare nullable
// forces "no open issues" and "the API call failed" to share the same null,
// which is precisely how an API failure ends up silently scored as a
// repository fact. The three narrow factories below make that conflation
// unrepresentable rather than merely discouraged.
//
// Carries only a Kind and, for Found, a single UTC timestamp: no token, no
// session, no repository owner/name, no API failure kind/message/body, no
// HTTP header or URL, no raw GraphQL data.
public sealed record OldestOpenIssueObservation
{
    public OldestOpenIssueObservationKind Kind { get; }

    // Non-null if and only if Kind == Found. Always UTC (offset zero) —
    // normalized in the Found factory below, which is the one place this
    // invariant is enforced, so no caller in any assembly can construct a
    // Found observation whose timestamp carries a non-zero offset.
    public DateTimeOffset? CreatedAtUtc { get; }

    private OldestOpenIssueObservation(OldestOpenIssueObservationKind kind, DateTimeOffset? createdAtUtc)
    {
        Kind = kind;
        CreatedAtUtc = createdAtUtc;
    }

    // A real open issue was found. The caller-supplied value is NOT assumed
    // to already be UTC (a parsed GitHub timestamp may carry any offset);
    // the instant is preserved exactly, only the offset representation
    // changes (e.g. 2026-08-30T15:00:00+03:00 becomes
    // 2026-08-30T12:00:00+00:00).
    public static OldestOpenIssueObservation Found(DateTimeOffset createdAtUtc) =>
        new(OldestOpenIssueObservationKind.Found, createdAtUtc.ToUniversalTime());

    // The query succeeded and confirmed zero open issues. Never carries a
    // timestamp — there is no issue to date.
    public static OldestOpenIssueObservation NoOpenIssues() =>
        new(OldestOpenIssueObservationKind.NoOpenIssues, createdAtUtc: null);

    // No trustworthy data. Never carries a timestamp, and deliberately
    // carries no failure reason either: the reason belongs to the layer
    // that produced it, and letting it travel into scoring would invite a
    // future "score failures differently" rule that this component
    // explicitly does not want.
    public static OldestOpenIssueObservation NoData() =>
        new(OldestOpenIssueObservationKind.NoData, createdAtUtc: null);
}
