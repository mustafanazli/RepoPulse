using System.Reflection;
using System.Runtime.CompilerServices;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-021: Faz 2's first pure scoring component for the Bakım (maintenance)
// sub-score — the age of the oldest OPEN issue. These tests exercise
// OldestOpenIssueAgeScorer's documented three-state input contract,
// applicability policy, exact TimeSpan threshold table and result
// invariants (see its doc comment and RepoPulse-Project-Plan.md's RP-021
// entry). No GitHub API call, no MAUI, no SQLite, no system clock — every
// "now" is supplied by the test itself.
public class OldestOpenIssueAgeScorerTests
{
    // A fixed, arbitrary reference instant. Nothing in the scorer depends on
    // the calendar date, so this is only here to keep the tests readable.
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------
    // A. Observation invariants
    // ---------------------------------------------------------------

    // 1. Found with an already-UTC (Z) timestamp keeps it in UTC.
    [Fact]
    public void FoundObservation_WithUtcTimestamp_KeepsUtcOffset()
    {
        var created = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        var observation = OldestOpenIssueObservation.Found(created);

        Assert.Equal(OldestOpenIssueObservationKind.Found, observation.Kind);
        Assert.NotNull(observation.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, observation.CreatedAtUtc!.Value.Offset);
        Assert.Equal(created, observation.CreatedAtUtc.Value);
    }

    // 2. Found with a positive offset is normalized to UTC, instant preserved.
    [Fact]
    public void FoundObservation_WithPositiveOffset_NormalizesToUtc()
    {
        var created = new DateTimeOffset(2026, 8, 30, 15, 0, 0, TimeSpan.FromHours(3));

        var observation = OldestOpenIssueObservation.Found(created);

        Assert.Equal(TimeSpan.Zero, observation.CreatedAtUtc!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), observation.CreatedAtUtc.Value);
        Assert.Equal(created.UtcTicks, observation.CreatedAtUtc.Value.UtcTicks);
    }

    // 3. Found with a negative offset is normalized to UTC, instant preserved.
    [Fact]
    public void FoundObservation_WithNegativeOffset_NormalizesToUtc()
    {
        var created = new DateTimeOffset(2026, 8, 30, 7, 0, 0, TimeSpan.FromHours(-5));

        var observation = OldestOpenIssueObservation.Found(created);

        Assert.Equal(TimeSpan.Zero, observation.CreatedAtUtc!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), observation.CreatedAtUtc.Value);
        Assert.Equal(created.UtcTicks, observation.CreatedAtUtc.Value.UtcTicks);
    }

    // 3b. Every offset the factory can be handed lands on the same instant.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-5)]
    [InlineData(9)]
    [InlineData(-11)]
    [InlineData(14)]
    public void FoundObservation_NormalizesAnyOffsetToUtc(int offsetHours)
    {
        var created = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
            .ToOffset(TimeSpan.FromHours(offsetHours));

        var observation = OldestOpenIssueObservation.Found(created);

        Assert.Equal(TimeSpan.Zero, observation.CreatedAtUtc!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), observation.CreatedAtUtc.Value);
    }

    // 4. NoOpenIssues carries no timestamp.
    [Fact]
    public void NoOpenIssuesObservation_CarriesNoTimestamp()
    {
        var observation = OldestOpenIssueObservation.NoOpenIssues();

        Assert.Equal(OldestOpenIssueObservationKind.NoOpenIssues, observation.Kind);
        Assert.Null(observation.CreatedAtUtc);
    }

    // 5. NoData carries no timestamp.
    [Fact]
    public void NoDataObservation_CarriesNoTimestamp()
    {
        var observation = OldestOpenIssueObservation.NoData();

        Assert.Equal(OldestOpenIssueObservationKind.NoData, observation.Kind);
        Assert.Null(observation.CreatedAtUtc);
    }

    // 6. NoOpenIssues and NoData are distinct observations — the whole point
    // of not using a bare nullable timestamp.
    [Fact]
    public void NoOpenIssuesAndNoData_AreDistinctObservations()
    {
        var noOpenIssues = OldestOpenIssueObservation.NoOpenIssues();
        var noData = OldestOpenIssueObservation.NoData();

        Assert.NotEqual(noOpenIssues.Kind, noData.Kind);
        Assert.NotEqual(noOpenIssues, noData);
    }

    // 7. OldestOpenIssueObservation has no public constructor.
    [Fact]
    public void Observation_HasNoPublicConstructor()
    {
        var constructors = typeof(OldestOpenIssueObservation).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    // 8. OldestOpenIssueObservation exposes no settable property — including
    // the `init` accessors a record would otherwise generate.
    [Fact]
    public void Observation_HasNoSettableProperty()
    {
        var properties = typeof(OldestOpenIssueObservation).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    // 9. No generic, arbitrary-state Create(...) factory exists anywhere on
    // OldestOpenIssueObservation (public or non-public).
    [Fact]
    public void Observation_HasNoGenericCreateFactory()
    {
        var methods = typeof(OldestOpenIssueObservation)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "Create");

        Assert.Empty(methods);
    }

    // 10. The only static factories are the three canonical ones, and none of
    // them lets a caller choose the Kind.
    [Fact]
    public void Observation_HasExactlyThreeCanonicalFactories()
    {
        var type = typeof(OldestOpenIssueObservation);
        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == type)
            .ToList();

        Assert.Equal(3, factories.Count);
        Assert.Contains(factories, f => f.Name == "Found");
        Assert.Contains(factories, f => f.Name == "NoOpenIssues");
        Assert.Contains(factories, f => f.Name == "NoData");
        Assert.All(factories, f => Assert.DoesNotContain(
            f.GetParameters(),
            p => p.ParameterType == typeof(OldestOpenIssueObservationKind)));
    }

    // 11. The observation carries nothing token/session/transport shaped.
    [Fact]
    public void Observation_CarriesNoTokenSessionOrApiShapedMember()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Client", "Secret", "Http", "Repository", "Url", "Header", "Message", "Body", "Owner" };

        var members = typeof(OldestOpenIssueObservation)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(typeof(OldestOpenIssueObservation)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => f.Name));

        foreach (var name in members)
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // 12. The scoring layer does not depend on the RP-020 API result type —
    // conversion is a future orchestration layer's job.
    [Fact]
    public void Scorer_DoesNotReferenceApiResultType()
    {
        var referencedTypes = typeof(OldestOpenIssueAgeScorer)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Concat(typeof(OldestOpenIssueObservation)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.PropertyType));

        Assert.All(referencedTypes, t =>
            Assert.NotEqual("RepoPulse.Core.Repositories", t.Namespace));
    }

    // ---------------------------------------------------------------
    // B. Applicability: archived / fork
    // ---------------------------------------------------------------

    // 13-21. Archived and/or fork is NotApplicable for every observation kind.
    [Theory]
    [InlineData(true, false, OldestOpenIssueObservationKind.Found)]
    [InlineData(true, false, OldestOpenIssueObservationKind.NoOpenIssues)]
    [InlineData(true, false, OldestOpenIssueObservationKind.NoData)]
    [InlineData(false, true, OldestOpenIssueObservationKind.Found)]
    [InlineData(false, true, OldestOpenIssueObservationKind.NoOpenIssues)]
    [InlineData(false, true, OldestOpenIssueObservationKind.NoData)]
    [InlineData(true, true, OldestOpenIssueObservationKind.Found)]
    [InlineData(true, true, OldestOpenIssueObservationKind.NoOpenIssues)]
    [InlineData(true, true, OldestOpenIssueObservationKind.NoData)]
    public void ArchivedOrFork_IsNotApplicableWithNullValue(bool isArchived, bool isFork, OldestOpenIssueObservationKind kind)
    {
        var observation = ObservationOfKind(kind);

        var result = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived, isFork);

        Assert.Equal(OldestOpenIssueAgeBand.NotApplicable, result.Band);
        Assert.Null(result.Value);
    }

    // 20. A fresh issue on an archived repository is NotApplicable, not
    // Fresh — applicability really does run before the age table.
    [Fact]
    public void ArchivedRepositoryWithFreshIssue_IsNotApplicableNotFresh()
    {
        var observation = OldestOpenIssueObservation.Found(NowUtc.AddDays(-1));

        var archived = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: true, isFork: false);
        var normal = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);

        Assert.Equal(OldestOpenIssueAgeBand.NotApplicable, archived.Band);
        Assert.Equal(OldestOpenIssueAgeBand.Fresh, normal.Band);
    }

    // ---------------------------------------------------------------
    // C. NoData: an API failure is never a zero
    // ---------------------------------------------------------------

    // 21. Normal repository + NoData -> NoData/null.
    [Fact]
    public void NormalRepositoryWithNoData_ScoresNoDataWithNullValue()
    {
        var result = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.NoData(), NowUtc, isArchived: false, isFork: false);

        Assert.Equal(OldestOpenIssueAgeBand.NoData, result.Band);
        Assert.Null(result.Value);
    }

    // 22. Structural proof that a missing signal is never converted into a
    // numeric score at all — not 0, and not the lowest band either.
    [Fact]
    public void NoData_IsNeverConvertedIntoANumericScore()
    {
        var result = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.NoData(), NowUtc, isArchived: false, isFork: false);

        Assert.Null(result.Value);
        Assert.NotEqual(0, result.Value ?? -1);
        Assert.NotEqual(OldestOpenIssueAgeBand.SeverelyStale, result.Band);
        Assert.NotEqual(OldestOpenIssueAgeBand.Stale, result.Band);
    }

    // 23. NoData and NotApplicable both carry a null Value but stay distinct
    // bands — "we don't know" and "this doesn't apply" are different facts.
    [Fact]
    public void NoDataAndNotApplicable_ShareNullValueButStayDistinctBands()
    {
        var noData = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.NoData(), NowUtc, isArchived: false, isFork: false);
        var notApplicable = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.NoData(), NowUtc, isArchived: true, isFork: false);

        Assert.Null(noData.Value);
        Assert.Null(notApplicable.Value);
        Assert.NotEqual(noData.Band, notApplicable.Band);
    }

    // ---------------------------------------------------------------
    // D. NoOpenIssues -> Clear
    // ---------------------------------------------------------------

    // 24. Normal repository + NoOpenIssues -> Clear/100.
    [Fact]
    public void NormalRepositoryWithNoOpenIssues_ScoresClearOneHundred()
    {
        var result = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.NoOpenIssues(), NowUtc, isArchived: false, isFork: false);

        Assert.Equal(OldestOpenIssueAgeBand.Clear, result.Band);
        Assert.Equal(100, result.Value);
    }

    // 25. Clear and Fresh score the same but are deliberately different
    // bands — a caller must be able to tell "no backlog at all" apart from
    // "a young backlog".
    [Fact]
    public void ClearAndFresh_ShareValueButStayDistinctBands()
    {
        var clear = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.NoOpenIssues(), NowUtc, isArchived: false, isFork: false);
        var fresh = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.Found(NowUtc.AddDays(-1)), NowUtc, isArchived: false, isFork: false);

        Assert.Equal(clear.Value, fresh.Value);
        Assert.NotEqual(clear.Band, fresh.Band);
        Assert.Equal(OldestOpenIssueAgeBand.Clear, clear.Band);
        Assert.Equal(OldestOpenIssueAgeBand.Fresh, fresh.Band);
    }

    // ---------------------------------------------------------------
    // E. Age thresholds (exact TimeSpan boundaries)
    // ---------------------------------------------------------------

    // 26. age == 0 -> Fresh/100.
    [Fact]
    public void ZeroAge_ScoresFreshOneHundred()
    {
        AssertBand(TimeSpan.Zero, OldestOpenIssueAgeBand.Fresh, 100);
    }

    // 27. age == exactly 30 days -> Fresh/100 (upper edge is inclusive).
    [Fact]
    public void ExactlyThirtyDays_ScoresFreshOneHundred()
    {
        AssertBand(TimeSpan.FromDays(30), OldestOpenIssueAgeBand.Fresh, 100);
    }

    // 28. age == 30 days + 1 tick -> Aging/75 (one tick past the edge).
    [Fact]
    public void ThirtyDaysPlusOneTick_ScoresAgingSeventyFive()
    {
        AssertBand(TimeSpan.FromDays(30) + TimeSpan.FromTicks(1), OldestOpenIssueAgeBand.Aging, 75);
    }

    // 29. age == exactly 90 days -> Aging/75.
    [Fact]
    public void ExactlyNinetyDays_ScoresAgingSeventyFive()
    {
        AssertBand(TimeSpan.FromDays(90), OldestOpenIssueAgeBand.Aging, 75);
    }

    // 30. age == 90 days + 1 tick -> Stale/40.
    [Fact]
    public void NinetyDaysPlusOneTick_ScoresStaleForty()
    {
        AssertBand(TimeSpan.FromDays(90) + TimeSpan.FromTicks(1), OldestOpenIssueAgeBand.Stale, 40);
    }

    // 31. age == exactly 180 days -> Stale/40.
    [Fact]
    public void ExactlyOneHundredEightyDays_ScoresStaleForty()
    {
        AssertBand(TimeSpan.FromDays(180), OldestOpenIssueAgeBand.Stale, 40);
    }

    // 32. age == 180 days + 1 tick -> SeverelyStale/10.
    [Fact]
    public void OneHundredEightyDaysPlusOneTick_ScoresSeverelyStaleTen()
    {
        AssertBand(TimeSpan.FromDays(180) + TimeSpan.FromTicks(1), OldestOpenIssueAgeBand.SeverelyStale, 10);
    }

    // 33. A very old issue -> SeverelyStale/10.
    [Fact]
    public void VeryOldIssue_ScoresSeverelyStaleTen()
    {
        AssertBand(TimeSpan.FromDays(5000), OldestOpenIssueAgeBand.SeverelyStale, 10);
    }

    // 34. Boundaries are exact TimeSpans, not truncated whole days: 30 days
    // plus a few hours is already Aging, and 90 days plus a few hours is
    // already Stale. A whole-day truncation would report Fresh/Aging here.
    [Theory]
    [InlineData(30, 1, OldestOpenIssueAgeBand.Aging, 75)]
    [InlineData(30, 23, OldestOpenIssueAgeBand.Aging, 75)]
    [InlineData(90, 1, OldestOpenIssueAgeBand.Stale, 40)]
    [InlineData(180, 1, OldestOpenIssueAgeBand.SeverelyStale, 10)]
    public void SubDayRemainderPastABoundary_MovesToTheNextBand(int days, int hours, OldestOpenIssueAgeBand expectedBand, int expectedValue)
    {
        AssertBand(TimeSpan.FromDays(days) + TimeSpan.FromHours(hours), expectedBand, expectedValue);
    }

    // 35. Full band sweep across the whole table in one place.
    [Theory]
    [InlineData(0, OldestOpenIssueAgeBand.Fresh, 100)]
    [InlineData(15, OldestOpenIssueAgeBand.Fresh, 100)]
    [InlineData(30, OldestOpenIssueAgeBand.Fresh, 100)]
    [InlineData(31, OldestOpenIssueAgeBand.Aging, 75)]
    [InlineData(60, OldestOpenIssueAgeBand.Aging, 75)]
    [InlineData(90, OldestOpenIssueAgeBand.Aging, 75)]
    [InlineData(91, OldestOpenIssueAgeBand.Stale, 40)]
    [InlineData(150, OldestOpenIssueAgeBand.Stale, 40)]
    [InlineData(180, OldestOpenIssueAgeBand.Stale, 40)]
    [InlineData(181, OldestOpenIssueAgeBand.SeverelyStale, 10)]
    [InlineData(365, OldestOpenIssueAgeBand.SeverelyStale, 10)]
    public void AgeInDays_MapsToDocumentedBandAndValue(int ageDays, OldestOpenIssueAgeBand expectedBand, int expectedValue)
    {
        AssertBand(TimeSpan.FromDays(ageDays), expectedBand, expectedValue);
    }

    // ---------------------------------------------------------------
    // F. Time safety: future dates, offsets, determinism, no clock read
    // ---------------------------------------------------------------

    // 36. A future-dated issue clamps to age zero -> Fresh/100, never throws.
    [Fact]
    public void FutureDatedIssue_ClampsToZeroAgeAndScoresFresh()
    {
        var observation = OldestOpenIssueObservation.Found(NowUtc.AddDays(30));

        var result = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);

        Assert.Equal(OldestOpenIssueAgeBand.Fresh, result.Band);
        Assert.Equal(100, result.Value);
    }

    // 37. Even a wildly future-dated issue is clamped rather than throwing.
    [Theory]
    [InlineData(1)]
    [InlineData(400)]
    [InlineData(100000)]
    public void AnyFutureDatedIssue_ScoresFreshWithoutThrowing(int futureDays)
    {
        var observation = OldestOpenIssueObservation.Found(NowUtc.AddDays(futureDays));

        var result = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);

        Assert.Equal(OldestOpenIssueAgeBand.Fresh, result.Band);
        Assert.Equal(100, result.Value);
    }

    // 38. The same instant expressed with different offsets — on either the
    // observation or nowUtc — produces the identical result.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 9)]
    [InlineData(-11, 14)]
    [InlineData(3, -8)]
    public void SameInstantWithDifferentOffsets_ProducesIdenticalResult(int createdOffsetHours, int nowOffsetHours)
    {
        var createdUtc = NowUtc.AddDays(-45);
        var observation = OldestOpenIssueObservation.Found(createdUtc.ToOffset(TimeSpan.FromHours(createdOffsetHours)));
        var now = NowUtc.ToOffset(TimeSpan.FromHours(nowOffsetHours));

        var shifted = OldestOpenIssueAgeScorer.Score(observation, now, isArchived: false, isFork: false);
        var baseline = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.Found(createdUtc), NowUtc, isArchived: false, isFork: false);

        Assert.Equal(baseline.Band, shifted.Band);
        Assert.Equal(baseline.Value, shifted.Value);
        Assert.Equal(OldestOpenIssueAgeBand.Aging, shifted.Band);
    }

    // 39. Determinism: the same observation and nowUtc always score the same.
    [Fact]
    public void SameObservationAndNow_ProducesIdenticalResultEveryTime()
    {
        var observation = OldestOpenIssueObservation.Found(NowUtc.AddDays(-120));

        var first = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);
        var second = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);
        var third = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
        Assert.Equal(OldestOpenIssueAgeBand.Stale, first.Band);
    }

    // 40. OldestOpenIssueAgeScorer never reads the system clock. Verified
    // against the source file so that a future edit reintroducing a hidden
    // clock read is caught even if it happens to produce a passing score in
    // every other test. Comment lines are stripped first, since the doc
    // comment legitimately *mentions* these APIs while documenting why they
    // must never be used.
    [Fact]
    public void Scorer_NeverReadsSystemClockDirectly()
    {
        var codeOnly = ScorerCodeWithoutComments();

        Assert.DoesNotContain("DateTime.Now", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.Now", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain(".UtcNow", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("Stopwatch", codeOnly, StringComparison.Ordinal);
    }

    // 41. The scorer never truncates the age to whole days, and never routes
    // the comparison through a floating-point or decimal day count.
    [Fact]
    public void Scorer_NeverUsesWholeDayOrFloatingPointComparison()
    {
        var codeOnly = ScorerCodeWithoutComments();

        Assert.DoesNotContain(".Days", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("TotalDays", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("double", codeOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decimal", codeOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("float", codeOnly, StringComparison.OrdinalIgnoreCase);
    }

    // 42. The scorer performs no I/O and reaches no transport/persistence
    // layer of any kind.
    [Fact]
    public void Scorer_ReferencesNoTransportOrPersistenceApi()
    {
        var codeOnly = ScorerCodeWithoutComments();

        Assert.DoesNotContain("HttpClient", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", codeOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GitHubApiClient", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("RepoPulse.Core.Repositories", codeOnly, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // G. Null argument
    // ---------------------------------------------------------------

    // 43. A null observation is a caller bug, not a scoreable state.
    [Fact]
    public void NullObservation_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OldestOpenIssueAgeScorer.Score(null!, NowUtc, isArchived: false, isFork: false));

        Assert.Equal("observation", exception.ParamName);
    }

    // 44. A null observation is rejected even for an archived/fork
    // repository — the applicability shortcut must not swallow a caller bug.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullObservation_ThrowsEvenForNotApplicableRepositories(bool isArchived, bool isFork)
    {
        Assert.Throws<ArgumentNullException>(
            () => OldestOpenIssueAgeScorer.Score(null!, NowUtc, isArchived, isFork));
    }

    // ---------------------------------------------------------------
    // H. Result invariants and metadata
    // ---------------------------------------------------------------

    // 45. ComponentId is exactly "oldest-open-issue-age".
    [Fact]
    public void ComponentId_IsExactlyOldestOpenIssueAge()
    {
        Assert.Equal("oldest-open-issue-age", OldestOpenIssueAgeScorer.ComponentId);
    }

    // 46. AlgorithmVersion is exactly "0.1.0", and every result carries it.
    [Fact]
    public void AlgorithmVersion_IsExactlyZeroDotOneDotZero()
    {
        Assert.Equal("0.1.0", OldestOpenIssueAgeScorer.AlgorithmVersion);

        var result = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservation.Found(NowUtc.AddDays(-10)), NowUtc, isArchived: false, isFork: false);

        Assert.Equal("0.1.0", result.AlgorithmVersion);
    }

    // 47. Both identifiers are compile-time const static string fields, not
    // mutable statics or properties a test/caller could reassign.
    [Theory]
    [InlineData(nameof(OldestOpenIssueAgeScorer.ComponentId))]
    [InlineData(nameof(OldestOpenIssueAgeScorer.AlgorithmVersion))]
    public void MetadataConstants_AreConstStaticStringFields(string fieldName)
    {
        var field = typeof(OldestOpenIssueAgeScorer).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral);
        Assert.False(field.IsInitOnly);
        Assert.Equal(typeof(string), field.FieldType);
    }

    // 48. OldestOpenIssueAgeScore carries no ComponentId property — the
    // identifier lives on the scorer, so there is no per-instance metadata
    // slot a caller could populate with a value of its own choosing.
    [Fact]
    public void Score_HasNoComponentIdProperty()
    {
        var properties = typeof(OldestOpenIssueAgeScore).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(properties, p => p.Name == "ComponentId");
    }

    // 49. OldestOpenIssueAgeScore has no public constructor.
    [Fact]
    public void Score_HasNoPublicConstructor()
    {
        var constructors = typeof(OldestOpenIssueAgeScore).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    // 50. OldestOpenIssueAgeScore exposes no settable property.
    [Fact]
    public void Score_HasNoSettableProperty()
    {
        var properties = typeof(OldestOpenIssueAgeScore).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    // 51. No generic, arbitrary-value Create(...) factory exists anywhere on
    // OldestOpenIssueAgeScore (public or non-public).
    [Fact]
    public void Score_HasNoGenericCreateFactory()
    {
        var methods = typeof(OldestOpenIssueAgeScore)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "Create");

        Assert.Empty(methods);
    }

    // 52. Every factory on OldestOpenIssueAgeScore is parameterless — no
    // caller-supplied Value, Band or AlgorithmVersion can ever reach it.
    [Fact]
    public void Score_FactoriesTakeNoParameters()
    {
        var type = typeof(OldestOpenIssueAgeScore);
        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == type)
            .ToList();

        Assert.NotEmpty(factories);
        Assert.All(factories, f => Assert.Empty(f.GetParameters()));
    }

    // 53. No public static factory on OldestOpenIssueAgeScore can produce an
    // instance — the only route to a score is
    // OldestOpenIssueAgeScorer.Score(...), which always builds a consistent
    // combination.
    [Fact]
    public void Score_HasNoPublicFactoryOfItsOwn()
    {
        var type = typeof(OldestOpenIssueAgeScore);
        var publicStaticFactories = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == type);

        Assert.Empty(publicStaticFactories);
    }

    // 54. Each of the seven internal factories produces exactly its own
    // canonical Value/Band/AlgorithmVersion combination (exercised directly,
    // since InternalsVisibleTo grants RepoPulse.UnitTests access).
    [Fact]
    public void EachFactory_ProducesItsCanonicalState()
    {
        AssertCanonical(OldestOpenIssueAgeScore.NotApplicable(), null, OldestOpenIssueAgeBand.NotApplicable);
        AssertCanonical(OldestOpenIssueAgeScore.NoData(), null, OldestOpenIssueAgeBand.NoData);
        AssertCanonical(OldestOpenIssueAgeScore.Clear(), 100, OldestOpenIssueAgeBand.Clear);
        AssertCanonical(OldestOpenIssueAgeScore.Fresh(), 100, OldestOpenIssueAgeBand.Fresh);
        AssertCanonical(OldestOpenIssueAgeScore.Aging(), 75, OldestOpenIssueAgeBand.Aging);
        AssertCanonical(OldestOpenIssueAgeScore.Stale(), 40, OldestOpenIssueAgeBand.Stale);
        AssertCanonical(OldestOpenIssueAgeScore.SeverelyStale(), 10, OldestOpenIssueAgeBand.SeverelyStale);

        static void AssertCanonical(OldestOpenIssueAgeScore score, int? expectedValue, OldestOpenIssueAgeBand expectedBand)
        {
            Assert.Equal(expectedValue, score.Value);
            Assert.Equal(expectedBand, score.Band);
            Assert.Equal(OldestOpenIssueAgeScorer.AlgorithmVersion, score.AlgorithmVersion);
        }
    }

    // 55. There is exactly one factory per band — no band is unreachable and
    // none has two competing constructions.
    [Fact]
    public void Score_HasExactlyOneFactoryPerBand()
    {
        var type = typeof(OldestOpenIssueAgeScore);
        var factoryNames = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == type)
            .Select(m => m.Name)
            .ToList();

        var bandNames = Enum.GetNames<OldestOpenIssueAgeBand>();

        Assert.Equal(bandNames.Length, factoryNames.Count);
        Assert.Equal(bandNames.Order(), factoryNames.Order());
    }

    // 56. No reachable input produces an invalid Value/Band combination: the
    // null-valued bands are exactly NotApplicable and NoData, and every
    // other band carries its one documented number.
    [Fact]
    public void NoReachableInput_ProducesAnInvalidValueBandCombination()
    {
        var expectedValues = new Dictionary<OldestOpenIssueAgeBand, int?>
        {
            [OldestOpenIssueAgeBand.NotApplicable] = null,
            [OldestOpenIssueAgeBand.NoData] = null,
            [OldestOpenIssueAgeBand.Clear] = 100,
            [OldestOpenIssueAgeBand.Fresh] = 100,
            [OldestOpenIssueAgeBand.Aging] = 75,
            [OldestOpenIssueAgeBand.Stale] = 40,
            [OldestOpenIssueAgeBand.SeverelyStale] = 10,
        };

        var observations = new[]
        {
            OldestOpenIssueObservation.NoData(),
            OldestOpenIssueObservation.NoOpenIssues(),
            OldestOpenIssueObservation.Found(NowUtc.AddDays(-1)),
            OldestOpenIssueObservation.Found(NowUtc.AddDays(-45)),
            OldestOpenIssueObservation.Found(NowUtc.AddDays(-120)),
            OldestOpenIssueObservation.Found(NowUtc.AddDays(-400)),
            OldestOpenIssueObservation.Found(NowUtc.AddDays(5)),
        };

        var producedBands = new HashSet<OldestOpenIssueAgeBand>();

        foreach (var observation in observations)
        {
            foreach (var isArchived in new[] { false, true })
            {
                foreach (var isFork in new[] { false, true })
                {
                    var result = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived, isFork);

                    Assert.True(expectedValues.ContainsKey(result.Band));
                    Assert.Equal(expectedValues[result.Band], result.Value);
                    Assert.Equal("0.1.0", result.AlgorithmVersion);
                    producedBands.Add(result.Band);
                }
            }
        }

        // Every band except Clear-vs-Fresh overlap is genuinely reachable
        // from the inputs above, so the sweep is not vacuously passing.
        Assert.Equal(Enum.GetValues<OldestOpenIssueAgeBand>().Length, producedBands.Count);
    }

    // 57. The score carries nothing token/session/transport shaped, and does
    // not smuggle `nowUtc` or repository identity into the result.
    [Fact]
    public void Score_CarriesNoTokenSessionOrApiShapedMember()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Client", "Secret", "Http", "Repository", "Url", "Header", "Body", "Owner", "Now", "Archived", "Fork" };

        var members = typeof(OldestOpenIssueAgeScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(typeof(OldestOpenIssueAgeScore)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.Name));

        foreach (var name in members)
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // 58. The score exposes exactly the three documented properties — no
    // extra surface has quietly appeared.
    [Fact]
    public void Score_ExposesExactlyValueBandAndAlgorithmVersion()
    {
        var propertyNames = typeof(OldestOpenIssueAgeScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order()
            .ToArray();

        Assert.Equal(new[] { "AlgorithmVersion", "Band", "Value" }, propertyNames);
    }

    // 59. Regression: RP-015/RP-017/RP-018/RP-019/RP-020 behaviour is
    // unaffected — covered by the shared full-assembly test run rather than
    // a dedicated test here.

    private static OldestOpenIssueObservation ObservationOfKind(OldestOpenIssueObservationKind kind) => kind switch
    {
        OldestOpenIssueObservationKind.Found => OldestOpenIssueObservation.Found(NowUtc.AddDays(-400)),
        OldestOpenIssueObservationKind.NoOpenIssues => OldestOpenIssueObservation.NoOpenIssues(),
        _ => OldestOpenIssueObservation.NoData()
    };

    private static void AssertBand(TimeSpan age, OldestOpenIssueAgeBand expectedBand, int expectedValue)
    {
        var observation = OldestOpenIssueObservation.Found(NowUtc - age);

        var result = OldestOpenIssueAgeScorer.Score(observation, NowUtc, isArchived: false, isFork: false);

        Assert.Equal(expectedBand, result.Band);
        Assert.Equal(expectedValue, result.Value);
    }

    private static string ScorerCodeWithoutComments()
    {
        var source = File.ReadAllLines(GetScorerSourcePath());
        return string.Join('\n', source.Where(line => !line.TrimStart().StartsWith("//")));
    }

    private static string GetScorerSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(
            testDirectory, "..", "..", "src", "RepoPulse.Core", "Scoring", "OldestOpenIssueAgeScorer.cs"));
    }
}
