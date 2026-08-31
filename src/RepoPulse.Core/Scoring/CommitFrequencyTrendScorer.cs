namespace RepoPulse.Core.Scoring;

// RP-018: Faz 2 / issue #13's third vertical slice for the Aktivite
// sub-score — a normalized commit-frequency TREND built purely from two of
// RP-016's own default-branch commit counts. Recency (RP-015), the raw
// 30-day frequency band (RP-017), the rest of Aktivite, every other
// sub-score, the weighted overall Health Score, and any UI/API/SQLite wiring
// are all deliberately out of scope — see the plan doc's RP-018 entry for
// the full boundary. This scorer makes NO GitHub API call of its own and
// adds NO new endpoint — it is a pure function over two already-obtained
// counts.
//
// Pure and MAUI/API/SQLite-independent: no GitHub API client, no HttpClient,
// no RepoPulse.Core.Repositories/Authentication model, and no system clock
// read of any kind.
//
// INPUT CONTRACT: both `last30DayCommitCount` and `last90DayCommitCount`
// must be the ALREADY-VALIDATED counts from two successful RP-016
// GetDefaultBranchCommitCountAsync calls (or an equivalent trusted source)
// — never raw API responses — and BOTH calls must share the exact same
// `untilUtc` instant. Because the two counts necessarily come from two
// separate HTTP requests (RP-016 makes exactly one request per count, by
// design, and never combines a 30-day and 90-day count into a single call),
// a repository can change between them — a new commit landing, a
// force-push, or a rebase — so `last90DayCommitCount < last30DayCommitCount`
// is a real possibility, not just a caller bug; see InconsistentData below.
//   - null (on either count) means "no count could be obtained" for that
//     call (its own RP-016 call did not return IsSuccess=true) — this is
//     NOT a zero score. It is reported as Band=NoData with Value=null, so a
//     future overall Health Score calculation can treat "we don't know"
//     differently from "we know it's zero" rather than silently averaging a
//     missing signal in as 0. Partial data (only one of the two counts
//     available) is never used to compute a trend.
//   - A negative non-null count can only come from a producer bug (RP-016's
//     own Success() factory already guards against ever producing a
//     negative Count) — Score() throws ArgumentOutOfRangeException rather
//     than silently clamping it, so such a bug is never hidden.
//
// OVERLAP CORRECTION: the 90-day count already includes the 30-day window,
// so the two raw counts are never compared directly. The non-overlapping
// "previous" period is derived as:
//     previous60Count = last90DayCommitCount - last30DayCommitCount
// and the trend compares the RECENT 30-day period against the PREVIOUS
// (non-overlapping) 60-day period that immediately precedes it.
//
// NORMALIZED RATE COMPARISON (integer-only, no floating point/decimal):
// the recent 30-day count is scaled to a 60-day-equivalent so the two
// differently-sized windows can be compared fairly:
//     recentEquivalent60   = (long)last30DayCommitCount * 2
//     previousEquivalent60 = previous60Count
// The two are then compared via long-based cross multiplication against a
// +/-25% band (see Score() below) — never by dividing into a float/decimal
// rate, which would introduce rounding and culture-formatting risk.
//
// KNOWN METRIC LIMITATIONS (deliberately not solved by this scorer):
//   - This is a bare commit-COUNT trend on the default branch only (per
//     RP-016's own scope) — it does not measure commit quality.
//   - The two counts are obtained from two separate API calls at two
//     (nearly, but not exactly) simultaneous moments — a genuine repository
//     change between them can legitimately produce InconsistentData; this
//     is not treated as an error condition to crash on.
//   - A force-push, rebase, or default-branch change between the two calls
//     can also produce a count relationship that does not reflect organic
//     development activity.
//   - No distinction is made between a genuine feature commit, a merge
//     commit, a bot/CI commit, or a series of small/fragmented commits.
//   - At small absolute counts, a percentage-based trend can be volatile
//     (e.g. going from 1 commit to 2 commits is a +100% swing) — this is an
//     inherent property of any ratio-based trend at low volume, not a bug.
//   - Because of these limitations, this result must only ever be used as
//     ONE component that feeds into the Aktivite sub-score — never
//     presented on its own as a "Genel Sağlık Puanı" or a repository's
//     overall health.
//   - If the banding/threshold policy below changes, AlgorithmVersion MUST
//     be bumped — existing stored scores must never be silently
//     reinterpreted under a new policy.
public static class CommitFrequencyTrendScorer
{
    // Component/algorithm identity — see RepoPulse-Project-Plan.md's
    // algorithm-versioning rules (§6). Intentionally a literal, not derived
    // from the assembly version or any runtime/build timestamp, so it never
    // drifts on its own.
    public const string ComponentId = "commit-frequency-trend";
    public const string AlgorithmVersion = "0.1.0";

    public const int RecentWindowDays = 30;
    public const int PreviousWindowDays = 60;
    public const int TotalWindowDays = 90;

    // The +/-25% comparison band, expressed as an exact integer ratio so the
    // cross-multiplication below never needs floating point:
    //   Accelerating: recentEquivalent60 / previousEquivalent60 >= 5/4 (+25%)
    //   Decelerating: recentEquivalent60 / previousEquivalent60 <= 3/4 (-25%)
    private const long RatioDenominator = 4;
    private const long AccelerationThresholdNumerator = 5;
    private const long DecelerationThresholdNumerator = 3;

    public static CommitFrequencyTrendScore Score(int? last30DayCommitCount, int? last90DayCommitCount)
    {
        if (last30DayCommitCount is null || last90DayCommitCount is null)
        {
            return CommitFrequencyTrendScore.NoData();
        }

        if (last30DayCommitCount.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(last30DayCommitCount), last30DayCommitCount.Value, "Commit count cannot be negative.");
        }

        if (last90DayCommitCount.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(last90DayCommitCount), last90DayCommitCount.Value, "Commit count cannot be negative.");
        }

        // All arithmetic from here on is long-based: the largest possible
        // intermediate value is int.MaxValue * 2 * 5, which stays far below
        // long.MaxValue — no overflow is possible for any valid int input.
        long count30 = last30DayCommitCount.Value;
        long count90 = last90DayCommitCount.Value;

        if (count90 < count30)
        {
            // The 90-day window is a strict superset of the 30-day window,
            // so count90 < count30 is impossible for two counts of the same
            // repository state taken at the same instant. Two separate API
            // calls can never be perfectly atomic, so this is treated as a
            // real (if rare) outcome — never an exception/crash.
            return CommitFrequencyTrendScore.InconsistentData();
        }

        long previousEquivalent60 = count90 - count30;
        long recentEquivalent60 = count30 * 2;

        if (previousEquivalent60 == 0)
        {
            return count30 > 0
                ? CommitFrequencyTrendScore.Accelerating()
                : CommitFrequencyTrendScore.StableInactive();
        }

        if (recentEquivalent60 * RatioDenominator >= previousEquivalent60 * AccelerationThresholdNumerator)
        {
            return CommitFrequencyTrendScore.Accelerating();
        }

        if (recentEquivalent60 * RatioDenominator <= previousEquivalent60 * DecelerationThresholdNumerator)
        {
            return CommitFrequencyTrendScore.Decelerating();
        }

        return CommitFrequencyTrendScore.Stable();
    }
}
