namespace RepoPulse.Core.Scoring;

// RP-015: the "son commit güncelliği" (last-commit-recency) component of the
// Aktivite sub-score (see RepoPulse-Project-Plan.md §6) — deliberately not the
// full Aktivite sub-score (commit frequency/trend are separate, later work)
// and never the weighted overall Health Score. Carries only what a caller
// needs to render or aggregate this one component: no repository identity,
// no token, no API response shape of any kind.
//
// Construction is intentionally not public: Value/Band/AlgorithmVersion form
// a single fixed-shape invariant (a given Band always carries one specific
// Value, AlgorithmVersion is always the scorer's own constant) that only
// ActivityRecencyScorer's threshold logic is allowed to produce. A public
// positional constructor would let any caller build an inconsistent result
// (e.g. Value=500 with Band=Fresh, or a fabricated AlgorithmVersion).
public sealed record ActivityRecencyScore
{
    public int Value { get; }
    public ActivityRecencyBand Band { get; }
    public string AlgorithmVersion { get; }

    private ActivityRecencyScore(int value, ActivityRecencyBand band, string algorithmVersion)
    {
        Value = value;
        Band = band;
        AlgorithmVersion = algorithmVersion;
    }

    internal static ActivityRecencyScore Create(int value, ActivityRecencyBand band, string algorithmVersion) =>
        new(value, band, algorithmVersion);
}
