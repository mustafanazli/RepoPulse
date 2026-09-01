namespace RepoPulse.Core.Scoring;

// RP-019: tells a caller (or a future explanation generator, issue #15)
// exactly which inputs the combined Activity result was actually built from
// — never inferable from Value/Band alone, since e.g. a Dormant score can
// come from a full 3-component calculation or from a 2-component partial one.
//
// Recency (RP-015) and Frequency (RP-017) are REQUIRED: each depends on
// exactly one successful upstream signal (a confirmed latest-commit read, a
// confirmed 30-day count) and their absence means "we know essentially
// nothing about this repository's activity" — the combined result is NoData.
// Trend (RP-018) is OPTIONAL: it depends on two separate API calls
// (a structurally more failure-prone shape, see CommitFrequencyTrendScorer's
// own doc comment on InconsistentData), so its absence still leaves two
// perfectly good signals — the combined result is produced anyway, with its
// weight reassigned to Recency/Frequency, and this enum records why.
public enum ActivityScoreCompleteness
{
    // All three components contributed; Trend's own weight (20) was used as-is.
    Full,

    // Trend was CommitFrequencyTrendBand.NoData (one or both of RP-016's
    // counts could not be obtained). Trend excluded, Recency/Frequency
    // weights (45/35, denominator 80) used unmodified.
    PartialTrendNoData,

    // Trend was CommitFrequencyTrendBand.InconsistentData (count90 < count30
    // — a race between two separate API calls, not a real trend). Treated
    // identically to PartialTrendNoData for weighting purposes.
    PartialTrendInconsistent,

    // Recency was null (upstream latest-commit data could not be obtained —
    // an API failure, NOT a confirmed "zero commits" result) while Frequency
    // was present. Combined result is NoData.
    MissingRequiredRecency,

    // Frequency was Band=NoData (RP-016's 30-day count could not be obtained)
    // while Recency was present. Combined result is NoData.
    MissingRequiredFrequency,

    // Both required components were missing. Combined result is NoData.
    MissingBothRequired
}
