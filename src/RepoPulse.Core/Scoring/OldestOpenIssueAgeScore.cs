namespace RepoPulse.Core.Scoring;

// RP-021: the "en eski açık issue yaşı" (oldest-open-issue age) component of
// the Bakım (maintenance) sub-score (see RepoPulse-Project-Plan.md §6) —
// deliberately not the full Bakım sub-score and never the weighted overall
// Health Score. Carries only what a caller needs to render or aggregate this
// one component: no repository identity, no token, no API response shape,
// no `nowUtc`, no raw issue data.
//
// Construction is intentionally not public-positional, and — following the
// RP-015/RP-017/RP-018 lesson that an internal-but-generic factory is still
// a real invariant gap — there is no generic internal factory either. Each
// of the seven bands is produced by its own parameterless internal factory
// that hard-codes its own Value/Band and always takes AlgorithmVersion from
// OldestOpenIssueAgeScorer's own constant, never from a caller. This makes
// an inconsistent OldestOpenIssueAgeScore (e.g. NoData with Value=0,
// NotApplicable with a non-null Value, Clear with Value=10, SeverelyStale
// with Value=100, or a fabricated version) structurally unreachable, not
// just unlikely.
//
// There is deliberately no ComponentId property here (matching RP-019's
// ActivityScore): a per-instance, caller-visible component identifier would
// be one more piece of metadata to keep consistent, and the identifier
// belongs to the scorer type, not to an individual result. Callers read
// OldestOpenIssueAgeScorer.ComponentId instead.
public sealed record OldestOpenIssueAgeScore
{
    // null for NotApplicable and NoData — "we are not scoring this" and "we
    // do not know" must never be flattened into a numeric 0, which a future
    // Bakım aggregation would otherwise average in as if it were a measured
    // fact.
    public int? Value { get; }

    public OldestOpenIssueAgeBand Band { get; }

    public string AlgorithmVersion { get; }

    private OldestOpenIssueAgeScore(int? value, OldestOpenIssueAgeBand band)
    {
        Value = value;
        Band = band;
        AlgorithmVersion = OldestOpenIssueAgeScorer.AlgorithmVersion;
    }

    internal static OldestOpenIssueAgeScore NotApplicable() =>
        new(value: null, OldestOpenIssueAgeBand.NotApplicable);

    internal static OldestOpenIssueAgeScore NoData() =>
        new(value: null, OldestOpenIssueAgeBand.NoData);

    internal static OldestOpenIssueAgeScore Clear() =>
        new(value: 100, OldestOpenIssueAgeBand.Clear);

    internal static OldestOpenIssueAgeScore Fresh() =>
        new(value: 100, OldestOpenIssueAgeBand.Fresh);

    internal static OldestOpenIssueAgeScore Aging() =>
        new(value: 75, OldestOpenIssueAgeBand.Aging);

    internal static OldestOpenIssueAgeScore Stale() =>
        new(value: 40, OldestOpenIssueAgeBand.Stale);

    internal static OldestOpenIssueAgeScore SeverelyStale() =>
        new(value: 10, OldestOpenIssueAgeBand.SeverelyStale);
}
