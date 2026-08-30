namespace RepoPulse.Core.Scoring;

// RP-017: Faz 2 / issue #13's next smallest vertical slice — only the
// "30 günlük commit sıklığı" (30-day commit frequency) piece of the
// Aktivite sub-score. Recency (RP-015), trend/90-day comparison, the rest
// of Aktivite, every other sub-score, the weighted overall Health Score,
// and any UI/API/SQLite wiring are all deliberately out of scope — see the
// plan doc's RP-017 entry for the full boundary.
//
// Pure and MAUI/API/SQLite-independent: no GitHub API client, no HttpClient,
// no RepoPulse.Core.Repositories/Authentication model, and no system clock
// read of any kind. This version supports ONLY a fixed 30-day window —
// WindowDays is not a caller parameter, so a different window can never be
// silently scored with these thresholds; a future window size needs its own
// AlgorithmVersion (and, if the banding structure itself changes, its own
// scorer).
//
// INPUT SEMANTICS: `commitCount` must be the ALREADY-VALIDATED count from a
// successful RP-016 GetDefaultBranchCommitCountAsync call (or an equivalent
// trusted source) — never a raw API response.
//   - null means "no count could be obtained" (the caller's RP-016 call did
//     not return IsSuccess=true) — this is NOT a zero score. It is reported
//     as Band=NoData with Value=null, so a future overall Health Score
//     calculation can treat "we don't know" differently from "we know it's
//     zero" rather than silently averaging a missing signal in as 0.
//   - 0 means the RP-016 call succeeded and found zero commits in the
//     window — a real, known Inactive signal, scored 0.
//   - A negative count can only come from a producer bug (RP-016's own
//     Success() factory already guards against ever producing a negative
//     Count) — Score() throws ArgumentOutOfRangeException rather than
//     silently clamping it, so such a bug is never hidden.
//
// KNOWN METRIC LIMITATIONS (deliberately not solved by this scorer):
//   - This is a bare commit-COUNT signal on the default branch only (per
//     RP-016's own scope) — it does not measure commit quality.
//   - A high count is not evidence of high code quality, and a low count is
//     not evidence of a poorly maintained repository.
//   - No distinction is made between a genuine feature commit, a merge
//     commit, a bot/CI commit, or a series of small/fragmented commits from
//     one change.
//   - Because of these limitations, this result must only ever be used as
//     ONE component that feeds into the Aktivite sub-score — never
//     presented on its own as a "Genel Sağlık Puanı" or a repository's
//     overall health.
//   - If the threshold table below changes, AlgorithmVersion MUST be
//     bumped — existing stored scores must never be silently reinterpreted
//     under a new table.
public static class CommitFrequencyScorer
{
    // Component/algorithm identity — see RepoPulse-Project-Plan.md's
    // algorithm-versioning rules (§6): a threshold/weight change here would
    // be a minor bump, a change to the banding structure itself a major
    // bump. Intentionally a literal, not derived from the assembly version
    // or any runtime/build timestamp, so it never drifts on its own.
    public const string ComponentId = "commit-frequency";
    public const string AlgorithmVersion = "0.1.0";
    public const int WindowDays = 30;

    public static CommitFrequencyScore Score(int? commitCount)
    {
        if (commitCount is null)
        {
            return CommitFrequencyScore.Create(null, CommitFrequencyBand.NoData, AlgorithmVersion, null, WindowDays);
        }

        if (commitCount.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commitCount), commitCount.Value, "Commit count cannot be negative.");
        }

        var count = commitCount.Value;

        if (count == 0)
        {
            return CommitFrequencyScore.Create(0, CommitFrequencyBand.Inactive, AlgorithmVersion, count, WindowDays);
        }

        if (count <= 4)
        {
            return CommitFrequencyScore.Create(40, CommitFrequencyBand.Low, AlgorithmVersion, count, WindowDays);
        }

        if (count <= 14)
        {
            return CommitFrequencyScore.Create(70, CommitFrequencyBand.Moderate, AlgorithmVersion, count, WindowDays);
        }

        // No arithmetic (addition/multiplication) is ever performed on
        // count — only comparisons — so an extreme value like int.MaxValue
        // can never overflow; it simply falls through to the highest band.
        return CommitFrequencyScore.Create(100, CommitFrequencyBand.High, AlgorithmVersion, count, WindowDays);
    }
}
