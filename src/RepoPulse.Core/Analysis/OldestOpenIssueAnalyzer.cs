using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;
using RepoPulse.Core.Scoring;

namespace RepoPulse.Core.Analysis;

// RP-023: the first place in production code where RP-020, RP-022 and
// RP-021 actually meet.
//
//   GetOldestOpenIssueAsync  (RP-020, data)
//     -> OldestOpenIssueObservationMapper.Map  (RP-022, adaptation)
//       -> OldestOpenIssueAgeScorer.Score  (RP-021, policy)
//         -> OldestOpenIssueAnalysisResult  (score + operational status)
//
// Until now each of those was complete, tested and unreachable: nothing in
// the application invoked any of them. This type is the orchestration slice
// their doc comments each defer to, for exactly ONE Bakim (maintenance)
// signal.
//
// SCOPE: one signal, no UI. The combined Bakim sub-score (which needs a
// second signal that does not exist yet), the weighted overall Health
// Score, the other three Bakim signals, Aktivite orchestration, MAUI/DI
// wiring, SQLite persistence and cross-component contradiction detection
// are all out of scope. No new endpoint is added and no scoring policy is
// changed here: this type owns sequencing and failure translation only.
//
// TOKEN: accessToken is a method parameter and nothing else. It is never
// stored in a field, cached, logged, copied into
// RepositoryAnalysisContext, or placed in the result. The caller is
// expected to read it immediately before the call (as
// RepositoryDetailPage already does for RP-013's latest-commit load) so
// that a token invalidated mid-run is never reused from a stale copy held
// here.
//
// UNAUTHORIZED IS REPORTED, NOT ACTED ON. This layer deliberately takes no
// UserSessionStore, SessionPersistenceStore or ISessionInvalidationMarker.
// Session invalidation is a UI-bound flow (RepositoryDetailPage's
// HandleInvalidSessionAsync), and giving a Core analysis type the power to
// sign a user out would make it stateful, untestable in isolation, and
// able to end a session as a side effect of a background measurement. It
// returns AnalysisSignalStatus.Unauthorized and lets the caller decide.
//
// STATELESS BY CONSTRUCTION. The only field is the injected client; there
// is no mutable instance or static state, so concurrent runs cannot
// interfere and a late-arriving result has nothing here to corrupt.
// Discarding a result that arrives after the session or the selected
// repository changed is therefore NOT this type's job and is not attempted
// here - that belongs to the calling coordinator, where the pattern
// already exists (LatestCommitLoadCoordinator's operation identity plus
// UserSessionStore.CaptureSnapshot/IsCurrent, RP-012/RP-013).
//
// NO SYSTEM CLOCK. analysisTimestampUtc always comes from the caller,
// matching every RP-015..RP-021 scorer, so a run is fully deterministic and
// so a future multi-signal run can give every component one shared
// reference instant.
public sealed class OldestOpenIssueAnalyzer
{
    private readonly IGitHubApiClient gitHubApiClient;

    public OldestOpenIssueAnalyzer(IGitHubApiClient gitHubApiClient)
    {
        ArgumentNullException.ThrowIfNull(gitHubApiClient);

        this.gitHubApiClient = gitHubApiClient;
    }

    // ACCESS TOKEN CONTRACT
    //
    //   - A NULL token is a caller/programming error, not a runtime
    //     outcome, and always throws ArgumentNullException — whatever the
    //     repository looks like. The applicability short-circuit below
    //     never masks it, because the guard runs first.
    //
    //   - An EMPTY or WHITESPACE token is NOT validated here. That rule
    //     belongs to GitHubApiClient, which already rejects it for this
    //     endpoint before opening a connection and returns a typed
    //     failure; duplicating the check would create a second, drifting
    //     copy of the same policy. For a normal repository the run
    //     therefore ends as a failure with no network request made at all.
    //
    //   - For an ARCHIVED or FORK repository no request is made in the
    //     first place, so an empty or whitespace token does not prevent
    //     the NotApplicableSkipped result. This is deliberate and safe:
    //     applicability is decided purely from repository metadata the
    //     caller already holds, so that result asserts nothing about
    //     GitHub-side data and requires no authenticated answer. An empty
    //     token is consequently NOT a guarantee of failure — this path is
    //     the exception.
    //
    //   - Cancellation outranks both: an already-cancelled run throws
    //     rather than producing any result, even on the archived/fork
    //     path and even with an empty token.
    //
    //   - The token is used for this one call and is never stored in a
    //     field, cached or logged.
    public async Task<OldestOpenIssueAnalysisResult> AnalyzeAsync(
        string accessToken,
        RepositoryAnalysisContext repository,
        DateTimeOffset analysisTimestampUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(repository);

        // Checked BEFORE the applicability short-circuit below, not after.
        // An already-cancelled run must stay cancelled whatever the
        // repository looks like: silently returning a NotApplicableSkipped
        // result for an archived repository would let a caller that has
        // already navigated away record an "answer" it never waited for.
        cancellationToken.ThrowIfCancellationRequested();

        if (repository.IsArchived || repository.IsFork)
        {
            // No request is made at all. The scorer's applicability policy
            // runs before it inspects the observation, so any request's
            // answer would be discarded - spending a network round trip and
            // a rate-limit unit to produce a value that is thrown away.
            //
            // The score is still produced by the real scorer, from a real
            // NoData observation, rather than by constructing a
            // NotApplicable score directly: NoData is the honest
            // observation here (we did not ask, so we know nothing), and
            // routing through OldestOpenIssueAgeScorer keeps that policy in
            // exactly one place. If RP-021 later refines which repositories
            // are NotApplicable, this path follows automatically instead of
            // drifting.
            var skippedScore = OldestOpenIssueAgeScorer.Score(
                OldestOpenIssueObservation.NoData(),
                analysisTimestampUtc,
                repository.IsArchived,
                repository.IsFork);

            return OldestOpenIssueAnalysisResult.NotApplicableSkipped(skippedScore, analysisTimestampUtc);
        }

        // The caller's token is passed straight through. A cancellation
        // surfaces as OperationCanceledException and is deliberately NOT
        // caught or translated into a failure status - an abandoned run is
        // not a failed measurement, and recording it as one would let a
        // navigation away from a page look like a broken repository.
        //
        // There is no broad catch here either. Network and HTTP faults are
        // already turned into typed failures by GitHubApiClient, so the only
        // exceptions that can reach this point are genuine defects (for
        // example the mapper's InvalidOperationException for a success
        // result with no timestamp), and swallowing those into
        // AnalysisSignalStatus.Failed would hide a bug behind a plausible
        // "GitHub had a problem" status.
        var apiResult = await gitHubApiClient
            .GetOldestOpenIssueAsync(accessToken, repository.Owner, repository.Name, cancellationToken)
            .ConfigureAwait(false);

        // Every outcome - success and failure alike - goes through the
        // mapper. Failures become NoData there, which is the whole point:
        // the failure reason must not reach the scorer, so the reason is
        // read separately below, from the API result, for Status only.
        var observation = OldestOpenIssueObservationMapper.Map(apiResult);

        var score = OldestOpenIssueAgeScorer.Score(
            observation,
            analysisTimestampUtc,
            repository.IsArchived,
            repository.IsFork);

        if (apiResult.IsSuccess)
        {
            return OldestOpenIssueAnalysisResult.Succeeded(score, analysisTimestampUtc);
        }

        return OldestOpenIssueAnalysisResult.Failure(ToStatus(apiResult.FailureKind), score, analysisTimestampUtc);
    }

    // The ONLY place a GitHubOldestOpenIssueFailureKind is read. It feeds
    // the operational status and never the observation or the score, so no
    // failure kind can ever influence a numeric value or band.
    //
    // The discard arm covers Unexpected and, just as importantly, any
    // failure kind added to RP-020 after this was written: an unrecognised
    // failure degrades to Failed rather than being mistaken for a specific,
    // actionable one. A null FailureKind cannot occur for an unsuccessful
    // GitHubOldestOpenIssueResult, but is handled by the same arm rather
    // than asserted, since guessing would be worse than reporting "we could
    // not answer".
    private static AnalysisSignalStatus ToStatus(GitHubOldestOpenIssueFailureKind? failureKind) => failureKind switch
    {
        GitHubOldestOpenIssueFailureKind.RepositoryUnavailable => AnalysisSignalStatus.RepositoryUnavailable,
        GitHubOldestOpenIssueFailureKind.Unauthorized => AnalysisSignalStatus.Unauthorized,
        GitHubOldestOpenIssueFailureKind.RateLimited => AnalysisSignalStatus.RateLimited,
        GitHubOldestOpenIssueFailureKind.NetworkError => AnalysisSignalStatus.NetworkUnavailable,
        _ => AnalysisSignalStatus.Failed
    };
}
