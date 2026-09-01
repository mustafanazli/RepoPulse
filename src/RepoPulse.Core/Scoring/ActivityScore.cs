namespace RepoPulse.Core.Scoring;

// RP-019: the combined "Aktivite" sub-score (see RepoPulse-Project-Plan.md
// §6) — a deterministic weighted combination of RP-015's ActivityRecencyScore,
// RP-017's CommitFrequencyScore, and RP-018's CommitFrequencyTrendScore. Never
// the weighted overall Health Score (that combines Aktivite with the other
// four sub-scores — Bakım/CI-CD/Dokümantasyon/Topluluk — and is separate,
// later work) and never a fresh GitHub API call of its own: this is a pure
// function over three already-produced component results.
//
// Construction is intentionally not public-positional, and — following the
// RP-015/RP-017/RP-018 lesson that an internal-but-generic factory is still a
// real invariant gap — there is no generic internal factory either. Exactly
// two narrow internal factories exist: NoData(completeness) for the three
// "a required component is missing" completeness reasons, and
// Scored(value, completeness) for the three "we produced a number"
// completeness reasons. Each validates its own completeness argument and
// rejects the other factory's reasons, and Scored derives Band from value
// internally — a caller can never construct e.g. NoData with a non-null
// Value, or Scored with a MissingRequired* completeness, or a Value/Band
// mismatch, or a fabricated AlgorithmVersion/component-version string.
public sealed record ActivityScore
{
    public int? Value { get; }
    public ActivityScoreBand Band { get; }
    public ActivityScoreCompleteness Completeness { get; }
    public string AlgorithmVersion { get; }
    public string RecencyAlgorithmVersion { get; }
    public string FrequencyAlgorithmVersion { get; }
    public string TrendAlgorithmVersion { get; }

    private ActivityScore(int? value, ActivityScoreBand band, ActivityScoreCompleteness completeness)
    {
        Value = value;
        Band = band;
        Completeness = completeness;
        AlgorithmVersion = ActivityScorer.AlgorithmVersion;
        RecencyAlgorithmVersion = ActivityRecencyScorer.AlgorithmVersion;
        FrequencyAlgorithmVersion = CommitFrequencyScorer.AlgorithmVersion;
        TrendAlgorithmVersion = CommitFrequencyTrendScorer.AlgorithmVersion;
    }

    internal static ActivityScore NoData(ActivityScoreCompleteness completeness)
    {
        if (completeness is not (ActivityScoreCompleteness.MissingRequiredRecency
            or ActivityScoreCompleteness.MissingRequiredFrequency
            or ActivityScoreCompleteness.MissingBothRequired))
        {
            throw new ArgumentOutOfRangeException(nameof(completeness), completeness,
                "NoData requires a missing-required-data completeness reason.");
        }

        return new(value: null, ActivityScoreBand.NoData, completeness);
    }

    internal static ActivityScore Scored(int value, ActivityScoreCompleteness completeness)
    {
        if (value < 0 || value > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Activity value must be between 0 and 100.");
        }

        if (completeness is not (ActivityScoreCompleteness.Full
            or ActivityScoreCompleteness.PartialTrendNoData
            or ActivityScoreCompleteness.PartialTrendInconsistent))
        {
            throw new ArgumentOutOfRangeException(nameof(completeness), completeness,
                "Scored requires a Full/PartialTrend* completeness reason.");
        }

        return new(value, ClassifyBand(value), completeness);
    }

    // Band boundaries are inclusive and mirror ActivityScorer's documented
    // table exactly; kept here (rather than duplicated in ActivityScorer) so
    // there is exactly one place a Value maps to a Band.
    private static ActivityScoreBand ClassifyBand(int value)
    {
        if (value <= 19) return ActivityScoreBand.Dormant;
        if (value <= 39) return ActivityScoreBand.Low;
        if (value <= 59) return ActivityScoreBand.Moderate;
        if (value <= 79) return ActivityScoreBand.Active;
        return ActivityScoreBand.HighlyActive;
    }
}
