namespace RepoPulse.Core.Scoring;

// RP-021: Faz 2's first pure scoring component for the Bakım (maintenance)
// sub-score — the age of the repository's OLDEST OPEN ISSUE, scored from
// data RP-020 already knows how to fetch (see RepoPulse-Project-Plan.md's
// RP-021 entry for the full boundary).
//
// This turn is the SCORER LAYER ONLY. The combined Bakım sub-score, the
// weighted overall Health Score, the CI/CD, Documentation and Community
// sub-scores, and any UI/MAUI, SQLite/cache, Azure/GHCR/AuthApi or API
// wiring are all deliberately out of scope. This scorer makes NO GitHub API
// call of its own and adds NO new endpoint — it is a pure function over an
// already-obtained OldestOpenIssueObservation.
//
// Pure and MAUI/API/SQLite-independent: no GitHub API client, no HttpClient,
// no RepoPulse.Core.Repositories/Authentication model, and no system clock
// read of any kind anywhere in this type — "now" is always supplied by the
// caller, so results are fully deterministic and independently testable
// without waiting on real time.
//
// THREE-STATE INPUT: the input is an OldestOpenIssueObservation, never a
// bare nullable timestamp. A nullable timestamp cannot tell "GitHub
// confirmed zero open issues" apart from "the query failed", and those two
// must never share an outcome. See OldestOpenIssueObservation's doc comment.
//
// API FAILURE IS NOT A ZERO: an observation of NoData produces Band=NoData
// with Value=null — never 0, and never the lowest band. A missing signal is
// not evidence of poor maintenance, and a future Bakım aggregation must be
// able to see the difference and exclude the component rather than average
// a fabricated 0 into the result.
//
// APPLICABILITY (v0.1.0):
//   - ARCHIVED repositories are NotApplicable (Value=null). An archived
//     repository carries no expectation of ongoing maintenance by
//     definition, so an untriaged backlog says nothing about its
//     stewardship; scoring it would penalise a repository for being
//     correctly and deliberately retired.
//   - FORK repositories are NotApplicable (Value=null) by default. A fork's
//     issue tracker is frequently disabled, unused, or effectively owned by
//     the upstream project, so open-issue age on a fork usually measures
//     upstream's backlog (or nothing at all) rather than this repository's
//     own maintenance. A later version may refine this once the fork's own
//     issue-tracker state can be observed.
//   - The applicability check runs BEFORE the observation is inspected, so
//     an archived/fork repository is NotApplicable regardless of whether
//     its observation is Found, NoOpenIssues or NoData. It never bypasses
//     the observation's own invariants — the type can only ever hold a
//     canonical state to begin with.
//
// FUTURE-DATED ISSUES: issue creation timestamps come from GitHub, not from
// the local clock, and clock skew between the two can make a real issue
// appear to be in the future relative to nowUtc. Rather than throwing or
// producing a negative age, this is clamped to an age of zero and scored as
// the freshest possible band (100/Fresh) — a clock-skew artifact must never
// be treated as evidence of a stale or broken backlog.
//
// KNOWN METRIC LIMITATIONS (deliberately not solved by this scorer):
//   - This measures ONE thing: how long the single oldest open issue has
//     been open. It does not measure issue priority, labels, assignees,
//     activity, last-comment date, reopen history, or how hard the issue is
//     to resolve.
//   - A long-lived roadmap, discussion, tracking or "good first issue"
//     entry — perfectly healthy things for a project to keep open on
//     purpose — will drag this component down. That is an inherent property
//     of an age-of-oldest metric, not a bug.
//   - Zero open issues is treated as healthy (Clear/100) FOR THIS COMPONENT
//     ONLY. On its own it proves nothing about maintenance quality: a
//     repository with a disabled issue tracker, or one that closes reports
//     without addressing them, also reports zero.
//   - Because of these limitations, this result must only ever be used as
//     ONE component feeding into the Bakım sub-score — never presented on
//     its own as a repository's overall maintenance quality or as a "Genel
//     Sağlık Puanı".
//   - Converting an RP-020 GitHubOldestOpenIssueResult into an
//     OldestOpenIssueObservation is a FUTURE orchestration layer's job, as
//     is aligning data taken from separate endpoints into one coherent
//     analysis run; neither is done here.
//   - If the banding/threshold or applicability policy below changes,
//     AlgorithmVersion MUST be bumped — existing stored scores must never
//     be silently reinterpreted under a new policy.
public static class OldestOpenIssueAgeScorer
{
    // Component/algorithm identity — see RepoPulse-Project-Plan.md's
    // algorithm-versioning rules (§6), matching the ComponentId pattern
    // already used by CommitFrequencyScorer, CommitFrequencyTrendScorer and
    // ActivityScorer. Intentionally literals, not derived from the assembly
    // version or any runtime/build timestamp, so they never drift on their
    // own.
    public const string ComponentId = "oldest-open-issue-age";
    public const string AlgorithmVersion = "0.1.0";

    // Exact TimeSpan boundaries, all inclusive on their upper edge:
    //   age <= 30d          -> Fresh/100
    //   30d  < age <= 90d   -> Aging/75
    //   90d  < age <= 180d  -> Stale/40
    //   180d < age          -> SeverelyStale/10
    // Compared as whole TimeSpans (tick precision), never via a whole-day
    // component or a fractional day count — day truncation would silently
    // move every boundary by up to 24 hours, and a fractional conversion
    // would add rounding and culture-formatting risk to a comparison that
    // needs neither.
    private static readonly TimeSpan FreshThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan AgingThreshold = TimeSpan.FromDays(90);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(180);

    public static OldestOpenIssueAgeScore Score(
        OldestOpenIssueObservation observation,
        DateTimeOffset nowUtc,
        bool isArchived,
        bool isFork)
    {
        ArgumentNullException.ThrowIfNull(observation);

        // Applicability first: an archived or forked repository is
        // NotApplicable whatever its observation says.
        if (isArchived || isFork)
        {
            return OldestOpenIssueAgeScore.NotApplicable();
        }

        switch (observation.Kind)
        {
            case OldestOpenIssueObservationKind.NoData:
                return OldestOpenIssueAgeScore.NoData();

            case OldestOpenIssueObservationKind.NoOpenIssues:
                return OldestOpenIssueAgeScore.Clear();

            case OldestOpenIssueObservationKind.Found:
                // CreatedAtUtc is non-null exactly when Kind is Found — the
                // observation's own factories guarantee it, so this is an
                // invariant read, not an unchecked assumption. Subtracting
                // two DateTimeOffset values compares absolute instants
                // regardless of either side's offset; nowUtc is normalized
                // anyway so the comparison is explicit at the call site.
                return ScoreAge(nowUtc.ToUniversalTime() - observation.CreatedAtUtc!.Value);

            default:
                // Unreachable for any value the observation's factories can
                // produce. Treated as "we cannot say" rather than a crash or
                // a fabricated numeric score, so that adding a future
                // observation kind can never silently score as 0.
                return OldestOpenIssueAgeScore.NoData();
        }
    }

    private static OldestOpenIssueAgeScore ScoreAge(TimeSpan age)
    {
        // Clock skew can put a real creation timestamp ahead of nowUtc.
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age <= FreshThreshold)
        {
            return OldestOpenIssueAgeScore.Fresh();
        }

        if (age <= AgingThreshold)
        {
            return OldestOpenIssueAgeScore.Aging();
        }

        if (age <= StaleThreshold)
        {
            return OldestOpenIssueAgeScore.Stale();
        }

        return OldestOpenIssueAgeScore.SeverelyStale();
    }
}
