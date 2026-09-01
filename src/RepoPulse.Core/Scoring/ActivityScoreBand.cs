namespace RepoPulse.Core.Scoring;

// RP-019: coarse, human-readable classification alongside ActivityScore.Value.
// NoData is distinct from every other band — it means the combined Activity
// result could not be produced at all (a required component, recency or
// frequency, was itself missing), not merely a low activity level. The other
// five bands are pure ranges over Value (see ActivityScorer's band table);
// they say nothing about completeness — a Dormant score can be either Full
// or PartialTrendNoData/PartialTrendInconsistent, see ActivityScore.Completeness.
public enum ActivityScoreBand
{
    NoData,
    Dormant,
    Low,
    Moderate,
    Active,
    HighlyActive
}
