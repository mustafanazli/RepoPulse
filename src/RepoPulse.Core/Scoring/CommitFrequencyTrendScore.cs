namespace RepoPulse.Core.Scoring;

// RP-018: the "normalize edilmiş commit sıklığı trendi" (normalized commit
// frequency trend) component of the Aktivite sub-score (see
// RepoPulse-Project-Plan.md §6) — built from two of RP-016's own default-branch
// commit counts (a 30-day count and a 90-day count, both ending at the same
// untilUtc), never a new API call. Deliberately not the full Aktivite
// sub-score (recency from RP-015, the raw 30-day frequency band from RP-017,
// and any recency+frequency+trend combination are separate, later work) and
// never the weighted overall Health Score. Carries only what a caller needs
// to render or aggregate this one component: no repository identity, no
// token, no API response shape, no raw commit counts.
//
// Construction is intentionally not public-positional, and — following the
// RP-015/RP-017 lesson that an internal-but-generic factory is still a real
// invariant gap — there is no generic internal factory either. Each of the
// six bands is produced by its own parameterless internal factory that
// hard-codes its own Value/Band and always takes AlgorithmVersion/the window
// metadata from CommitFrequencyTrendScorer's own constants, never from a
// caller. This makes an inconsistent CommitFrequencyTrendScore (e.g.
// NoData with Value=0, InconsistentData with a non-null Value,
// StableInactive with Value=60, Accelerating with Value=25, Stable with
// Value=100, Decelerating with Value=60, or a fabricated version/window)
// structurally unreachable, not just unlikely.
public sealed record CommitFrequencyTrendScore
{
    public int? Value { get; }
    public CommitFrequencyTrendBand Band { get; }
    public string AlgorithmVersion { get; }
    public int RecentWindowDays { get; }
    public int PreviousWindowDays { get; }
    public int TotalWindowDays { get; }

    private CommitFrequencyTrendScore(int? value, CommitFrequencyTrendBand band)
    {
        Value = value;
        Band = band;
        AlgorithmVersion = CommitFrequencyTrendScorer.AlgorithmVersion;
        RecentWindowDays = CommitFrequencyTrendScorer.RecentWindowDays;
        PreviousWindowDays = CommitFrequencyTrendScorer.PreviousWindowDays;
        TotalWindowDays = CommitFrequencyTrendScorer.TotalWindowDays;
    }

    internal static CommitFrequencyTrendScore NoData() =>
        new(value: null, CommitFrequencyTrendBand.NoData);

    internal static CommitFrequencyTrendScore InconsistentData() =>
        new(value: null, CommitFrequencyTrendBand.InconsistentData);

    internal static CommitFrequencyTrendScore StableInactive() =>
        new(value: 0, CommitFrequencyTrendBand.StableInactive);

    internal static CommitFrequencyTrendScore Accelerating() =>
        new(value: 100, CommitFrequencyTrendBand.Accelerating);

    internal static CommitFrequencyTrendScore Stable() =>
        new(value: 60, CommitFrequencyTrendBand.Stable);

    internal static CommitFrequencyTrendScore Decelerating() =>
        new(value: 25, CommitFrequencyTrendBand.Decelerating);
}
