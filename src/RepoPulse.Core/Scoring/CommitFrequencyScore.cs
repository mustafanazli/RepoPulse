namespace RepoPulse.Core.Scoring;

// RP-017: the "30 günlük commit sıklığı" (30-day commit frequency)
// component of the Aktivite sub-score (see RepoPulse-Project-Plan.md §6) —
// deliberately not the full Aktivite sub-score (recency, from RP-015, is a
// separate component; trend/90-day comparison are separate, later work) and
// never the weighted overall Health Score. Carries only what a caller needs
// to render or aggregate this one component: no repository identity, no
// token, no API response shape of any kind.
//
// Construction is intentionally not public-positional: Value/Band/
// AlgorithmVersion/ObservedCommitCount/WindowDays form a single fixed-shape
// invariant (a given Band always carries one specific Value or null,
// ObservedCommitCount is null exactly when Band is NoData, WindowDays is
// always the scorer's own constant) that only CommitFrequencyScorer's
// threshold logic is allowed to produce. A public positional constructor
// would let any caller build an inconsistent result (e.g. Band=NoData with
// Value=0, or Band=Low with Value=100, or a fabricated WindowDays).
//
// RP-017 hardening: a generic internal Create(value, band, version, count,
// windowDays) factory is deliberately NOT provided — being internal is not
// enough, since any code inside RepoPulse.Core could still call it with an
// arbitrary/inconsistent combination (e.g. NoData with a non-null count, or
// a fabricated AlgorithmVersion/WindowDays). Instead there is one narrow
// factory per band; each one hard-codes its own Value, accepts (at most) the
// ObservedCommitCount that band actually varies by, rejects any count
// outside that band's own range, and always takes AlgorithmVersion/
// WindowDays from CommitFrequencyScorer's own constants — never from a
// caller. This makes an inconsistent CommitFrequencyScore structurally
// unreachable, not just unlikely.
public sealed record CommitFrequencyScore
{
    public int? Value { get; }
    public CommitFrequencyBand Band { get; }
    public string AlgorithmVersion { get; }
    public int? ObservedCommitCount { get; }
    public int WindowDays { get; }

    private CommitFrequencyScore(int? value, CommitFrequencyBand band, int? observedCommitCount)
    {
        Value = value;
        Band = band;
        AlgorithmVersion = CommitFrequencyScorer.AlgorithmVersion;
        ObservedCommitCount = observedCommitCount;
        WindowDays = CommitFrequencyScorer.WindowDays;
    }

    internal static CommitFrequencyScore NoData() =>
        new(value: null, CommitFrequencyBand.NoData, observedCommitCount: null);

    internal static CommitFrequencyScore Inactive() =>
        new(value: 0, CommitFrequencyBand.Inactive, observedCommitCount: 0);

    internal static CommitFrequencyScore Low(int observedCommitCount)
    {
        if (observedCommitCount < CommitFrequencyScorer.LowMinCommitCount || observedCommitCount > CommitFrequencyScorer.LowMaxCommitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(observedCommitCount), observedCommitCount,
                $"Low band requires a commit count between {CommitFrequencyScorer.LowMinCommitCount} and {CommitFrequencyScorer.LowMaxCommitCount}.");
        }

        return new(value: 40, CommitFrequencyBand.Low, observedCommitCount);
    }

    internal static CommitFrequencyScore Moderate(int observedCommitCount)
    {
        if (observedCommitCount < CommitFrequencyScorer.ModerateMinCommitCount || observedCommitCount > CommitFrequencyScorer.ModerateMaxCommitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(observedCommitCount), observedCommitCount,
                $"Moderate band requires a commit count between {CommitFrequencyScorer.ModerateMinCommitCount} and {CommitFrequencyScorer.ModerateMaxCommitCount}.");
        }

        return new(value: 70, CommitFrequencyBand.Moderate, observedCommitCount);
    }

    internal static CommitFrequencyScore High(int observedCommitCount)
    {
        if (observedCommitCount < CommitFrequencyScorer.HighMinCommitCount)
        {
            throw new ArgumentOutOfRangeException(nameof(observedCommitCount), observedCommitCount,
                $"High band requires a commit count of at least {CommitFrequencyScorer.HighMinCommitCount}.");
        }

        return new(value: 100, CommitFrequencyBand.High, observedCommitCount);
    }
}
