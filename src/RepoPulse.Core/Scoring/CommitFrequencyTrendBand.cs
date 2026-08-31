namespace RepoPulse.Core.Scoring;

// RP-018: coarse, human-readable classification alongside
// CommitFrequencyTrendScore.Value. NoData and InconsistentData are both
// "we can't say" outcomes but for different reasons — NoData means one or
// both of the two RP-016 counts could not be obtained at all;
// InconsistentData means both counts were obtained but the 90-day count was
// smaller than the 30-day count, which is impossible for two counts of the
// same superset relationship and points at a race between the two API calls
// (or a force-push/rebase/branch change) rather than a real trend.
// StableInactive is deliberately distinct from Stable — it means both the
// recent and previous periods had zero commits (a real, known "still
// inactive" signal), not merely a similar non-zero rate.
public enum CommitFrequencyTrendBand
{
    NoData,
    InconsistentData,
    StableInactive,
    Accelerating,
    Stable,
    Decelerating
}
