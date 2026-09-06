namespace RepoPulse.Core.Analysis;

// RP-023: the OPERATIONAL outcome of running one analysis signal, kept
// strictly separate from that signal's SCORE.
//
// WHY THIS EXISTS AT ALL: RP-022's OldestOpenIssueObservationMapper
// deliberately collapses every RP-020 failure kind into a single NoData
// observation, because the scoring layer must never be able to score the
// health of our own API session. That collapse is correct for scoring and
// lossy for everything else: after it, "the token is no longer valid",
// "we are rate limited", "the network is down" and "this repository is not
// visible to us" are indistinguishable. A caller needs those apart — an
// Unauthorized run must trigger the existing session-invalidation flow
// (RP-008), while a RateLimited or NetworkUnavailable run is safely
// retryable and should say so. This enum is where that distinction is
// preserved, alongside — never inside — the score.
//
// It is deliberately NOT GitHubOldestOpenIssueFailureKind re-exported.
// That type belongs to one GraphQL endpoint; this one belongs to the
// analysis layer, so later Bakım/Aktivite signals reading different
// endpoints can report into the same vocabulary instead of forcing callers
// to switch over one enum per endpoint. It also expresses two states no
// failure kind can: a successful run, and a run that was deliberately not
// made at all.
//
// Carries no token, no repository identity, no HTTP status code, no
// response body, no header, no URL and no exception message — a caller
// holding one of these values learns what to DO next, never what was on
// the wire.
public enum AnalysisSignalStatus
{
    // The GitHub query answered. The score was produced from real data —
    // either a found issue or a positively confirmed empty backlog.
    Succeeded,

    // The signal does not apply to this repository (v0.1.0: archived or
    // fork), so NO API request was made. This is not a failure and not a
    // missing answer: it is a deliberate skip, and it is distinct from
    // every failure value below precisely so a caller never renders it as
    // an error or retries it.
    NotApplicableSkipped,

    // The repository does not exist OR the token cannot see it. Following
    // GitHubOldestOpenIssueFailureKind.RepositoryUnavailable, this makes no
    // claim about which — the GraphQL null-repository shape cannot tell
    // them apart. Distinct from NotApplicableSkipped: there, we could see
    // the repository and chose not to ask.
    RepositoryUnavailable,

    // The access token was rejected. The caller — not this layer — should
    // run the existing session-invalidation flow; see
    // OldestOpenIssueAnalyzer's doc comment for why that decision stays at
    // the call site.
    Unauthorized,

    // GitHub refused the request for rate-limit reasons. Safe to retry
    // later; the caller can say so honestly.
    RateLimited,

    // The request could not reach GitHub or complete (connection failure,
    // timeout). Safe to retry; not evidence about the repository.
    NetworkUnavailable,

    // Every other unsuccessful outcome, including any failure kind added to
    // GitHubOldestOpenIssueFailureKind after this enum was written. A new
    // unrecognised failure must degrade to "we could not answer", never be
    // silently mistaken for one of the specific, actionable values above.
    Failed
}
