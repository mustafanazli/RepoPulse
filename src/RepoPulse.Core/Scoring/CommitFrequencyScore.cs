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
public sealed record CommitFrequencyScore
{
    public int? Value { get; }
    public CommitFrequencyBand Band { get; }
    public string AlgorithmVersion { get; }
    public int? ObservedCommitCount { get; }
    public int WindowDays { get; }

    private CommitFrequencyScore(int? value, CommitFrequencyBand band, string algorithmVersion, int? observedCommitCount, int windowDays)
    {
        Value = value;
        Band = band;
        AlgorithmVersion = algorithmVersion;
        ObservedCommitCount = observedCommitCount;
        WindowDays = windowDays;
    }

    internal static CommitFrequencyScore Create(int? value, CommitFrequencyBand band, string algorithmVersion, int? observedCommitCount, int windowDays) =>
        new(value, band, algorithmVersion, observedCommitCount, windowDays);
}
