namespace RepoPulse.Core.Scoring;

// RP-021: coarse, human-readable classification alongside
// OldestOpenIssueAgeScore.Value — a UI or report can show
// "Fresh"/"SeverelyStale" without re-deriving it from the numeric value or
// duplicating the threshold table.
//
// NotApplicable, NoData and Clear are three genuinely different "not a
// normal age band" outcomes and are deliberately kept apart:
//   - NotApplicable: the signal does not apply to this repository at all
//     (v0.1.0: archived or fork) — Value is null.
//   - NoData: the signal applies but no trustworthy observation exists —
//     Value is null. Never 0.
//   - Clear: the signal applies, the data IS trustworthy, and it says there
//     is no open-issue backlog at all — Value is 100. Clear carries the same
//     Value as Fresh but is a distinct band on purpose: "no open issues"
//     and "an open issue that is 3 days old" are different facts about a
//     repository even though this one component rewards them equally.
public enum OldestOpenIssueAgeBand
{
    NotApplicable,
    NoData,
    Clear,
    Fresh,
    Aging,
    Stale,
    SeverelyStale
}
