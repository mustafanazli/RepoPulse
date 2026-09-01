namespace RepoPulse.Core.Scoring;

// RP-019: Faz 2 / issue #13's Aktivite sub-score composition — combines
// RP-015's ActivityRecencyScore, RP-017's CommitFrequencyScore, and RP-018's
// CommitFrequencyTrendScore into one deterministic "activity" result. Every
// other sub-score (Bakım/CI-CD/Dokümantasyon/Topluluk), the weighted overall
// Health Score, and any UI/API/SQLite wiring are all deliberately out of
// scope — see the plan doc's RP-019 entry for the full boundary. This scorer
// makes NO GitHub API call of its own, calls no 30/90-day counting logic
// itself, and adds NO new endpoint — it is a pure function over three
// already-produced component results.
//
// Pure and MAUI/API/SQLite-independent: no GitHub API client, no HttpClient,
// no RepoPulse.Core.Repositories/Authentication model, and no system clock
// read of any kind — same discipline as its three inputs.
//
// COMPONENT DATA SOURCES (documented here so a caller wires the right
// upstream call to the right parameter):
//   - Recency: produced by ActivityRecencyScorer.Score from RP-013's latest-
//     commit data (RP-015). Does NOT depend on RP-016 in any way.
//   - Frequency: produced by CommitFrequencyScorer.Score from RP-016's
//     successful 30-day commit count (RP-017).
//   - Trend: produced by CommitFrequencyTrendScorer.Score from RP-016's two
//     commit counts (30-day and 90-day) taken at the same untilUtc (RP-018).
//
// RECENCY NULL SEMANTICS (the one place this scorer's null contract differs
// from its component's own null contract): ActivityRecencyScore itself never
// represents an API failure — ActivityRecencyScorer.Score(null, nowUtc)
// means GitHub POSITIVELY CONFIRMED zero commits, a valid 0/NoCommits
// result, and a caller must never even call that scorer for a genuine
// latest-commit API failure (see ActivityRecencyScorer's own doc comment).
// So here, at the COMPOSITION layer, `recencyScore == null` means something
// different: "the caller could not produce an ActivityRecencyScore at all"
// — i.e. the upstream latest-commit call itself failed. A confirmed
// "no commits" result is passed in as a real (non-null) ActivityRecencyScore
// with Value=0/Band=NoCommits, and is NOT treated as missing data below.
public static class ActivityScorer
{
    // Component/algorithm identity — see RepoPulse-Project-Plan.md's
    // algorithm-versioning rules (§6). Bump this if the weights, the
    // completeness/reweighting policy, the rounding rule, or the band
    // boundaries below ever change — existing stored Activity results must
    // never be silently reinterpreted under a new policy. This version is
    // independent of RecencyAlgorithmVersion/FrequencyAlgorithmVersion/
    // TrendAlgorithmVersion (carried on the result) — if any of those three
    // sub-algorithms changes, this composition's correctness against the new
    // sub-algorithm should be re-reviewed, but the version numbers are not
    // coupled to each other.
    public const string AlgorithmVersion = "0.1.0";

    // Recency is weighted highest: it is the single most direct "is this
    // repository still alive" signal and depends on exactly one upstream
    // call. Frequency is second: also a single upstream call, but a raw
    // 30-day count is coarser than a recency read. Trend is weighted lowest:
    // it depends on TWO separate upstream calls (a structurally more
    // failure-prone shape — see CommitFrequencyTrendScorer's own doc comment
    // on InconsistentData) and is, by its own documented nature, volatile at
    // small absolute commit counts.
    public const long RecencyWeight = 45;
    public const long FrequencyWeight = 35;
    public const long TrendWeight = 20;
    public const long TotalWeight = RecencyWeight + FrequencyWeight + TrendWeight;

    // Used only when Trend is unusable (NoData/InconsistentData): Recency
    // and Frequency's ORIGINAL weights (45/35) are kept as-is and simply
    // divided by their own sum (80) instead of the full 100 — this is
    // mathematically identical to proportionally redistributing Trend's
    // dropped weight between them (45+45*20/80=56.25%, 35+35*20/80=43.75% of
    // the original 100), without ever materializing an approximate integer
    // "effective weight" or letting the two partial weights drift from
    // summing to exactly this denominator.
    private const long PartialWeightDenominator = RecencyWeight + FrequencyWeight;

    // INPUT CONSISTENCY CONTRACT (documents a limitation; changes no behavior
    // and adds no runtime check). Score is a pure composition function, not an
    // orchestration layer: it makes no GitHub API call of its own and has no
    // way to verify that recencyScore/frequencyScore/trendScore describe the
    // same underlying repository state. GitHub's separate REST endpoints
    // (latest commit; 30-day count; 90-day count) offer no transactional or
    // atomic multi-endpoint snapshot guarantee — a push, force-push, rebase,
    // or ordinary propagation delay between the calls that produced these
    // three inputs can leave each one individually valid while together
    // describing different moments of the same repository. For example:
    // Recency=NoCommits paired with Frequency=Low/Moderate/High, or
    // Recency=Fresh/Recent paired with Frequency=Inactive.
    //
    // PRECONDITION (v0.1.0): the caller — a future analysis-orchestration
    // layer, explicitly out of RP-019's scope — is responsible for producing
    // these three inputs from one analysis run: capturing a single
    // analysisTimestampUtc, using that same untilUtc for RP-016's 30/90-day
    // counts (already required by CommitFrequencyTrendScorer), gathering the
    // recency/latest-commit read as close to that same run as practical, and
    // confirming all three results belong to the same repository/default-
    // branch identity. Aligning inputs to one analysis run REDUCES the chance
    // of a cross-component contradiction; it does NOT guarantee full
    // consistency — GitHub's endpoints remain independent, non-transactional
    // calls, and a contradiction can still occur even within one run.
    //
    // This method does not detect or reject cross-component contradictions:
    // given three individually-valid inputs it always composes them into
    // Full or PartialTrend* — it never emits a result for cross-component
    // disagreement (PartialTrendInconsistent is unrelated: it is Trend's own
    // internal 30-vs-90 disagreement, see CommitFrequencyTrendScorer, not a
    // disagreement between Recency/Frequency/Trend). Detecting contradictions
    // such as NoCommits-recency-with-non-zero-frequency or Fresh/Recent-
    // recency-with-Inactive-frequency, and deciding how to respond — at most
    // a bounded, safe re-fetch, with any persisting contradiction surfaced as
    // a typed Inconsistent/NoData analysis result rather than silently
    // producing a Full score — is deferred to that future orchestration layer
    // and is out of scope here.
    public static ActivityScore Score(
        ActivityRecencyScore? recencyScore,
        CommitFrequencyScore frequencyScore,
        CommitFrequencyTrendScore trendScore)
    {
        ArgumentNullException.ThrowIfNull(frequencyScore);
        ArgumentNullException.ThrowIfNull(trendScore);

        var recencyMissing = recencyScore is null;
        var frequencyMissing = frequencyScore.Band == CommitFrequencyBand.NoData;

        if (recencyMissing && frequencyMissing)
        {
            return ActivityScore.NoData(ActivityScoreCompleteness.MissingBothRequired);
        }

        if (recencyMissing)
        {
            return ActivityScore.NoData(ActivityScoreCompleteness.MissingRequiredRecency);
        }

        if (frequencyMissing)
        {
            return ActivityScore.NoData(ActivityScoreCompleteness.MissingRequiredFrequency);
        }

        // Both required components are present past this point.
        // ActivityRecencyScore.Value is a non-nullable int by its own
        // design. CommitFrequencyScore.Value is int? only because of
        // NoData; RP-017's factories guarantee it is non-null for every
        // other band (Inactive=0, Low/Moderate/High are all non-null), and
        // frequencyMissing (Band==NoData) has already been ruled out above.
        long recencyValue = recencyScore!.Value;
        long frequencyValue = frequencyScore.Value!.Value;

        var trendUnusable = trendScore.Band is CommitFrequencyTrendBand.NoData or CommitFrequencyTrendBand.InconsistentData;

        if (!trendUnusable)
        {
            // Same reasoning as frequencyValue above: RP-018's factories
            // guarantee a non-null Value for every band except NoData/
            // InconsistentData, both already ruled out by trendUnusable.
            long trendValue = trendScore.Value!.Value;

            long weightedSum = (recencyValue * RecencyWeight) + (frequencyValue * FrequencyWeight) + (trendValue * TrendWeight);
            var combined = (int)((weightedSum + (TotalWeight / 2)) / TotalWeight);

            return ActivityScore.Scored(combined, ActivityScoreCompleteness.Full);
        }

        var completeness = trendScore.Band == CommitFrequencyTrendBand.NoData
            ? ActivityScoreCompleteness.PartialTrendNoData
            : ActivityScoreCompleteness.PartialTrendInconsistent;

        long availableWeightedSum = (recencyValue * RecencyWeight) + (frequencyValue * FrequencyWeight);
        var partialCombined = (int)((availableWeightedSum + (PartialWeightDenominator / 2)) / PartialWeightDenominator);

        return ActivityScore.Scored(partialCombined, completeness);
    }
}
