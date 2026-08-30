namespace RepoPulse.Core.Scoring;

// RP-017: coarse, human-readable classification alongside CommitFrequencyScore.Value.
// NoData is deliberately distinct from Inactive — NoData means the caller
// could not obtain a commit count at all (see CommitFrequencyScorer's doc
// comment), Inactive means the count was successfully obtained and is zero.
public enum CommitFrequencyBand
{
    NoData,
    Inactive,
    Low,
    Moderate,
    High
}
