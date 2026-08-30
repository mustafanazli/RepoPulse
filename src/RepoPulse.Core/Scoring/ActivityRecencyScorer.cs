namespace RepoPulse.Core.Scoring;

// RP-015: Faz 2 / issue #13's smallest vertical slice — only the
// "son commit güncelliği" (last-commit-recency) piece of the Aktivite
// sub-score (RepoPulse-Project-Plan.md §6). Commit frequency (30/90-day),
// development trend, the rest of Aktivite, every other sub-score, the
// weighted overall Health Score, and any UI/API/SQLite wiring are all
// deliberately out of scope for this component — see the plan doc's RP-015
// entry for the full boundary.
//
// Pure and MAUI/API/SQLite-independent: no GitHub API client, no
// RepoPulse.Core.Repositories model, no HttpClient, and no system clock read
// of any kind (no DateTimeOffset.UtcNow / DateTime.Now anywhere in this
// type) — "now" is always supplied by the caller, so results are fully
// deterministic and independently testable without waiting on real time.
//
// NULL SEMANTICS: `lastCommitUtc == null` means ONE thing only — GitHub
// positively confirmed the repository has zero commits (the same shape
// GitHubLatestCommitResult.NoCommits() represents). It must NEVER be used to
// represent "data unavailable" (an API failure, rate limit, network error, or
// a malformed response) — that is an entirely different situation and must
// never be scored at all: a caller holding a GitHubLatestCommitFailureKind
// (or any other data-unavailable state) must keep that as its own, separate
// failure state and simply not call Score() for it, rather than collapsing
// it into null and letting it silently score as "no commits" (0).
//
// FUTURE-DATED COMMITS: commit timestamps come from Git metadata the local
// clock does not control, and a misconfigured clock can make a real commit
// appear to be in the future relative to `nowUtc`. Rather than throwing or
// producing a negative age, this is clamped to an age of zero and scored as
// the freshest possible band (100/Fresh) — a clock-skew artifact must never
// be treated as evidence of a stale or broken repository.
public static class ActivityRecencyScorer
{
    // Component/algorithm identity — see RepoPulse-Project-Plan.md's
    // algorithm-versioning rules (§6): a threshold/weight change here would
    // be a minor bump, a change to the banding structure itself a major
    // bump. Intentionally a literal, not derived from the assembly version
    // or any runtime/build timestamp, so it never drifts on its own.
    public const string AlgorithmVersion = "0.1.0";

    private static readonly TimeSpan FreshThreshold = TimeSpan.FromDays(7);
    private static readonly TimeSpan RecentThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan AgingThreshold = TimeSpan.FromDays(90);

    public static ActivityRecencyScore Score(DateTimeOffset? lastCommitUtc, DateTimeOffset nowUtc)
    {
        if (lastCommitUtc is null)
        {
            return ActivityRecencyScore.Create(0, ActivityRecencyBand.NoCommits, AlgorithmVersion);
        }

        // DateTimeOffset subtraction compares absolute instants regardless of
        // either value's offset, so the result is identical no matter what
        // time zone/culture either DateTimeOffset was constructed with.
        var age = nowUtc - lastCommitUtc.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age <= FreshThreshold)
        {
            return ActivityRecencyScore.Create(100, ActivityRecencyBand.Fresh, AlgorithmVersion);
        }

        if (age <= RecentThreshold)
        {
            return ActivityRecencyScore.Create(75, ActivityRecencyBand.Recent, AlgorithmVersion);
        }

        if (age <= AgingThreshold)
        {
            return ActivityRecencyScore.Create(40, ActivityRecencyBand.Aging, AlgorithmVersion);
        }

        return ActivityRecencyScore.Create(10, ActivityRecencyBand.Stale, AlgorithmVersion);
    }
}
