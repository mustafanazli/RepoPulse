using RepoPulse.Core.Scoring;

namespace RepoPulse.Core.Analysis;

// RP-023: what one oldest-open-issue analysis run hands back — the score
// AND, separately, what operationally happened while producing it.
//
// WHY BOTH: returning only OldestOpenIssueAgeScore would be lossy in a way
// that matters. RP-022's mapper turns every failure into NoData, so an
// expired token, a rate limit, a network outage and an invisible
// repository would all arrive as Band=NoData with nothing to tell them
// apart — leaving a caller unable to invalidate the session when it must,
// or to offer a safe retry when it can. Status carries exactly that, and
// carries it ALONGSIDE the score, never inside it: the numeric value and
// band are produced solely by OldestOpenIssueAgeScorer from an
// observation, and no failure reason ever reaches them.
//
// Score is always non-null. A failed run still has a well-defined score —
// NoData/null — because "we could not measure it" is a real, canonical
// scoring outcome in RP-021, not an absence to be modelled with a null
// reference.
//
// DELIBERATELY CARRIES NOTHING ELSE: no access token or session, no
// repository owner/name, no RepositoryAnalysisContext, no
// GitHubOldestOpenIssueResult, no GitHubOldestOpenIssueFailureKind, no
// exception or message, no URL/header/body, no CancellationToken and no
// API client. A caller holding this value cannot leak anything beyond a
// score, a coarse status and the timestamp it was told to analyse at.
public sealed record OldestOpenIssueAnalysisResult
{
    public AnalysisSignalStatus Status { get; }

    public OldestOpenIssueAgeScore Score { get; }

    // Always UTC (offset zero) — normalized in the constructor below, the
    // one place this invariant is enforced. This is the timestamp the
    // CALLER supplied for the run, echoed back so a stored or rendered
    // score can be labelled with the instant it was computed against, and
    // so a future multi-signal run can prove every component shared one
    // reference time. It is never read from the system clock here.
    public DateTimeOffset AnalysisTimestampUtc { get; }

    private OldestOpenIssueAnalysisResult(AnalysisSignalStatus status, OldestOpenIssueAgeScore score, DateTimeOffset analysisTimestampUtc)
    {
        Status = status;
        Score = score;
        AnalysisTimestampUtc = analysisTimestampUtc.ToUniversalTime();
    }

    // Three narrow internal factories, no generic Create, no public or init
    // setters — and, because every property is get-only and a sealed
    // record's generated copy constructor is private, no `with` can rewrite
    // a Status/Score pair into an inconsistent one from outside either.
    //
    // Each factory VALIDATES the combination rather than trusting that the
    // analyzer currently calls it correctly. The analyzer is one caller
    // today; these invariants are what make the type safe for every caller
    // it will have later, and a validation here fails loudly at the seam
    // instead of silently shipping a score whose status contradicts it.

    // A real answer: the query succeeded and the score came from data.
    internal static OldestOpenIssueAnalysisResult Succeeded(OldestOpenIssueAgeScore score, DateTimeOffset analysisTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (score.Band is not (OldestOpenIssueAgeBand.Clear
            or OldestOpenIssueAgeBand.Fresh
            or OldestOpenIssueAgeBand.Aging
            or OldestOpenIssueAgeBand.Stale
            or OldestOpenIssueAgeBand.SeverelyStale))
        {
            throw new ArgumentException(
                "A succeeded analysis must carry a scored band, never NotApplicable or NoData.",
                nameof(score));
        }

        if (score.Value is null)
        {
            throw new ArgumentException(
                "A succeeded analysis must carry a numeric score value.",
                nameof(score));
        }

        return new OldestOpenIssueAnalysisResult(AnalysisSignalStatus.Succeeded, score, analysisTimestampUtc);
    }

    // The signal does not apply to this repository, so no request was made.
    internal static OldestOpenIssueAnalysisResult NotApplicableSkipped(OldestOpenIssueAgeScore score, DateTimeOffset analysisTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (score.Band is not OldestOpenIssueAgeBand.NotApplicable || score.Value is not null)
        {
            throw new ArgumentException(
                "A skipped analysis must carry the NotApplicable band with no numeric value.",
                nameof(score));
        }

        return new OldestOpenIssueAnalysisResult(AnalysisSignalStatus.NotApplicableSkipped, score, analysisTimestampUtc);
    }

    // The query could not be answered. The reason survives in Status only;
    // the score is always NoData/null, exactly as the mapper and scorer
    // produced it.
    internal static OldestOpenIssueAnalysisResult Failure(AnalysisSignalStatus status, OldestOpenIssueAgeScore score, DateTimeOffset analysisTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (status is AnalysisSignalStatus.Succeeded or AnalysisSignalStatus.NotApplicableSkipped)
        {
            throw new ArgumentException(
                "A failed analysis must not claim a succeeded or skipped status.",
                nameof(status));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException(
                "A failed analysis must carry a defined analysis status.",
                nameof(status));
        }

        if (score.Band is not OldestOpenIssueAgeBand.NoData || score.Value is not null)
        {
            throw new ArgumentException(
                "A failed analysis must carry the NoData band with no numeric value.",
                nameof(score));
        }

        return new OldestOpenIssueAnalysisResult(status, score, analysisTimestampUtc);
    }
}
