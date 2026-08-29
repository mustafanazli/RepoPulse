namespace RepoPulse.Core.Scoring;

// RP-015: the "son commit güncelliği" (last-commit-recency) component of the
// Aktivite sub-score (see RepoPulse-Project-Plan.md §6) — deliberately not the
// full Aktivite sub-score (commit frequency/trend are separate, later work)
// and never the weighted overall Health Score. Carries only what a caller
// needs to render or aggregate this one component: no repository identity,
// no token, no API response shape of any kind.
public sealed record ActivityRecencyScore(int Value, ActivityRecencyBand Band, string AlgorithmVersion);
