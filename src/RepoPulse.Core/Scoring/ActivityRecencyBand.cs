namespace RepoPulse.Core.Scoring;

// RP-015: coarse, human-readable classification alongside ActivityRecencyScore.Value —
// a UI or report can show "Fresh"/"Stale" without re-deriving it from the numeric
// value or duplicating the threshold table.
public enum ActivityRecencyBand
{
    // GitHub confirmed the repository has zero commits — never used for a
    // data-unavailable/API-failure case (see ActivityRecencyScorer's doc comment).
    NoCommits,
    Fresh,
    Recent,
    Aging,
    Stale
}
