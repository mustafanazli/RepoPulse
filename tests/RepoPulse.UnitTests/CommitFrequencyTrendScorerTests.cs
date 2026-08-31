using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-018: Faz 2 / issue #13's third vertical slice for the Aktivite
// sub-score — a normalized commit-frequency TREND built purely from two of
// RP-016's own default-branch commit counts (a 30-day count and a 90-day
// count sharing the same untilUtc). These tests exercise
// CommitFrequencyTrendScorer's documented overlap-correction, normalized
// rate comparison, and threshold table (see its doc comment and
// RepoPulse-Project-Plan.md's RP-018 entry) with exact long-based integer
// arithmetic — no floating point/decimal anywhere.
public class CommitFrequencyTrendScorerTests
{
    // 1. null/null -> NoData.
    [Fact]
    public void BothCountsNull_ScoresNoData()
    {
        var result = CommitFrequencyTrendScorer.Score(null, null);

        Assert.Null(result.Value);
        Assert.Equal(CommitFrequencyTrendBand.NoData, result.Band);
    }

    // 2. null/10 -> NoData.
    [Fact]
    public void Only90DayCountPresent_ScoresNoData()
    {
        var result = CommitFrequencyTrendScorer.Score(null, 10);

        Assert.Null(result.Value);
        Assert.Equal(CommitFrequencyTrendBand.NoData, result.Band);
    }

    // 3. 5/null -> NoData.
    [Fact]
    public void Only30DayCountPresent_ScoresNoData()
    {
        var result = CommitFrequencyTrendScorer.Score(5, null);

        Assert.Null(result.Value);
        Assert.Equal(CommitFrequencyTrendBand.NoData, result.Band);
    }

    // 4. NoData's Value is null (partial data is never used to compute a trend).
    [Fact]
    public void NoDataBand_HasNullValue()
    {
        var result = CommitFrequencyTrendScorer.Score(null, 42);

        Assert.Equal(CommitFrequencyTrendBand.NoData, result.Band);
        Assert.Null(result.Value);
    }

    // 5. Negative 30-day count -> controlled rejection, never silently clamped.
    [Fact]
    public void NegativeThirtyDayCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitFrequencyTrendScorer.Score(-1, 10));
    }

    // 6. Negative 90-day count -> controlled rejection.
    [Fact]
    public void NegativeNinetyDayCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitFrequencyTrendScorer.Score(1, -1));
    }

    // 7. int.MinValue on either parameter is rejected as negative.
    [Theory]
    [InlineData(int.MinValue, 10)]
    [InlineData(1, int.MinValue)]
    public void IntMinValueCount_ThrowsArgumentOutOfRangeException(int? last30, int? last90)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitFrequencyTrendScorer.Score(last30, last90));
    }

    // 8. count30=5, count90=4 -> InconsistentData (90-day window can never be
    // smaller than the 30-day window it contains).
    [Fact]
    public void NinetyDayCountLessThanThirtyDayCount_ScoresInconsistentData()
    {
        var result = CommitFrequencyTrendScorer.Score(5, 4);

        Assert.Equal(CommitFrequencyTrendBand.InconsistentData, result.Band);
    }

    // 9. InconsistentData's Value is null.
    [Fact]
    public void InconsistentDataBand_HasNullValue()
    {
        var result = CommitFrequencyTrendScorer.Score(5, 4);

        Assert.Null(result.Value);
    }

    // 10. Inconsistent data (count90 < count30) never throws or crashes —
    // it is a real, expected outcome of two non-atomic API calls.
    [Fact]
    public void InconsistentData_DoesNotThrow()
    {
        var exception = Record.Exception(() => CommitFrequencyTrendScorer.Score(5, 4));

        Assert.Null(exception);
    }

    // 11. 0/0 -> StableInactive/0 (both periods genuinely zero, not "unknown").
    [Fact]
    public void BothPeriodsZero_ScoresStableInactive()
    {
        var result = CommitFrequencyTrendScorer.Score(0, 0);

        Assert.Equal(0, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.StableInactive, result.Band);
    }

    // 12. 1/1 -> previous60=0, count30>0 -> Accelerating/100.
    [Fact]
    public void OneAndOne_PreviousPeriodEmptyWithActivity_ScoresAccelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(1, 1);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    // 13. 15/15 -> previous60=0, count30>0 -> Accelerating/100.
    [Fact]
    public void FifteenAndFifteen_PreviousPeriodEmptyWithActivity_ScoresAccelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(15, 15);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    // 14. count30=10, count90=30 -> previous60=20, recentEquivalent60=20 -> Stable/60.
    [Fact]
    public void SameNormalizedRate_ScoresStable()
    {
        var result = CommitFrequencyTrendScorer.Score(10, 30);

        Assert.Equal(60, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Stable, result.Band);
    }

    // 15. Exact +25% boundary: count30=5, previous60=8 (count90=13),
    // recentEquivalent60=10 -> Accelerating/100 (boundary is inclusive).
    [Fact]
    public void ExactPlusTwentyFivePercentBoundary_ScoresAccelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(5, 13);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    // 16. Just below the +25% boundary (previous60=9 instead of the exact-
    // boundary 8, one unit past it) -> Stable.
    [Fact]
    public void JustBelowPlusTwentyFivePercentBoundary_ScoresStable()
    {
        var result = CommitFrequencyTrendScorer.Score(5, 14);

        Assert.Equal(60, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Stable, result.Band);
    }

    // 17. A strong increase -> Accelerating.
    [Fact]
    public void StrongIncrease_ScoresAccelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(20, 25);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    // 18. Previous period empty, recent period active -> Accelerating (a
    // second, distinct example from 12/13).
    [Fact]
    public void PreviousPeriodEmptyRecentActive_ScoresAccelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(7, 7);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    // 19. Exact -25% boundary: count30=3, previous60=8 (count90=11),
    // recentEquivalent60=6 -> Decelerating/25 (boundary is inclusive).
    [Fact]
    public void ExactMinusTwentyFivePercentBoundary_ScoresDecelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(3, 11);

        Assert.Equal(25, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Decelerating, result.Band);
    }

    // 20. Just above the -25% boundary (previous60=7 instead of the exact-
    // boundary 8, one unit short of it) -> Stable.
    [Fact]
    public void JustAboveMinusTwentyFivePercentBoundary_ScoresStable()
    {
        var result = CommitFrequencyTrendScorer.Score(3, 10);

        Assert.Equal(60, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Stable, result.Band);
    }

    // 21. Recent period empty, previous period active -> Decelerating.
    [Fact]
    public void RecentPeriodEmptyPreviousActive_ScoresDecelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(0, 10);

        Assert.Equal(25, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Decelerating, result.Band);
    }

    // 22. A strong decrease -> Decelerating.
    [Fact]
    public void StrongDecrease_ScoresDecelerating()
    {
        var result = CommitFrequencyTrendScorer.Score(1, 41);

        Assert.Equal(25, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Decelerating, result.Band);
    }

    // 23. int.MaxValue/int.MaxValue -> a safe, correct result (previous60=0,
    // count30>0 -> Accelerating) without overflow or an exception.
    [Fact]
    public void MaxValueBothCounts_ScoresSafelyWithoutOverflow()
    {
        var result = CommitFrequencyTrendScorer.Score(int.MaxValue, int.MaxValue);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    // 24. Large but internally consistent counts (well below int.MaxValue,
    // but large enough that naive int-based cross multiplication would
    // overflow) still produce a correct, non-throwing result.
    [Fact]
    public void LargeConsistentCounts_DoNotOverflow()
    {
        var result = CommitFrequencyTrendScorer.Score(700_000_000, 2_000_000_000);

        Assert.Equal(60, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Stable, result.Band);
    }

    // 25. Same inputs produce a deterministic result.
    [Fact]
    public void SameInputs_ProduceDeterministicResult()
    {
        var first = CommitFrequencyTrendScorer.Score(5, 13);
        var second = CommitFrequencyTrendScorer.Score(5, 13);

        Assert.Equal(first, second);
    }

    // 26. A different current culture must not affect the result — the
    // scorer uses only long-based integer comparisons, never
    // culture-sensitive formatting or parsing.
    [Fact]
    public void DifferentCurrentCulture_ScoresIdentically()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantResult = CommitFrequencyTrendScorer.Score(10, 30);

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turkishResult = CommitFrequencyTrendScorer.Score(10, 30);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var germanResult = CommitFrequencyTrendScorer.Score(10, 30);

            Assert.Equal(invariantResult, turkishResult);
            Assert.Equal(invariantResult, germanResult);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // 27. The scorer never uses floating point or decimal arithmetic.
    [Fact]
    public void Scorer_NeverUsesFloatingPointOrDecimal()
    {
        var source = File.ReadAllLines(GetScorerSourcePath());
        var codeOnly = string.Join('\n', source.Where(line => !line.TrimStart().StartsWith("//")));

        Assert.DoesNotContain("double", codeOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decimal", codeOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("float", codeOnly, StringComparison.OrdinalIgnoreCase);
    }

    // 28. CommitFrequencyTrendScorer never reads the system clock.
    [Fact]
    public void Scorer_NeverReadsSystemClockDirectly()
    {
        var source = File.ReadAllLines(GetScorerSourcePath());
        var codeOnly = string.Join('\n', source.Where(line => !line.TrimStart().StartsWith("//")));

        Assert.DoesNotContain("DateTime.Now", codeOnly);
        Assert.DoesNotContain("DateTimeOffset.Now", codeOnly);
        Assert.DoesNotContain(".UtcNow", codeOnly);
        Assert.DoesNotContain("DateTime.Today", codeOnly);
    }

    // 29. ComponentId is exactly "commit-frequency-trend".
    [Fact]
    public void ComponentId_IsExactlyCommitFrequencyTrend()
    {
        Assert.Equal("commit-frequency-trend", CommitFrequencyTrendScorer.ComponentId);
    }

    // 30. AlgorithmVersion is exactly "0.1.0".
    [Fact]
    public void AlgorithmVersion_IsExactlyZeroDotOneDotZero()
    {
        Assert.Equal("0.1.0", CommitFrequencyTrendScorer.AlgorithmVersion);

        var result = CommitFrequencyTrendScorer.Score(10, 30);
        Assert.Equal("0.1.0", result.AlgorithmVersion);
    }

    // 31. Window constants are exactly 30/60/90, and every result carries them.
    [Fact]
    public void WindowConstants_AreExactlyThirtySixtyNinety()
    {
        Assert.Equal(30, CommitFrequencyTrendScorer.RecentWindowDays);
        Assert.Equal(60, CommitFrequencyTrendScorer.PreviousWindowDays);
        Assert.Equal(90, CommitFrequencyTrendScorer.TotalWindowDays);

        var result = CommitFrequencyTrendScorer.Score(10, 30);
        Assert.Equal(30, result.RecentWindowDays);
        Assert.Equal(60, result.PreviousWindowDays);
        Assert.Equal(90, result.TotalWindowDays);
    }

    // 32. CommitFrequencyTrendScore has no public constructor.
    [Fact]
    public void CommitFrequencyTrendScore_HasNoPublicConstructor()
    {
        var constructors = typeof(CommitFrequencyTrendScore).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(constructors);
    }

    // 33. No generic, arbitrary-value Create(...) factory exists anywhere on
    // CommitFrequencyTrendScore (public or internal).
    [Fact]
    public void CommitFrequencyTrendScore_HasNoGenericCreateFactory()
    {
        var methods = typeof(CommitFrequencyTrendScore)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "Create");

        Assert.Empty(methods);
    }

    // 34. None of CommitFrequencyTrendScore's static factories accepts any
    // caller-supplied Value, Band, AlgorithmVersion, or window — every
    // factory that returns a CommitFrequencyTrendScore is parameterless.
    [Fact]
    public void CommitFrequencyTrendScore_FactoriesTakeNoParameters()
    {
        var type = typeof(CommitFrequencyTrendScore);
        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == type)
            .ToList();

        Assert.NotEmpty(factories);
        Assert.All(factories, f => Assert.Empty(f.GetParameters()));
    }

    // 34b. No public static factory on CommitFrequencyTrendScore itself can
    // produce an instance — the only route to a CommitFrequencyTrendScore is
    // CommitFrequencyTrendScorer.Score(int?, int?), which always builds a
    // consistent combination.
    [Fact]
    public void CommitFrequencyTrendScore_HasNoPublicFactoryOfItsOwn()
    {
        var type = typeof(CommitFrequencyTrendScore);
        var publicStaticFactories = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == type);

        Assert.Empty(publicStaticFactories);
    }

    // 35. Each of the six internal factories directly produces its own
    // canonical, documented state (exercised directly, since
    // InternalsVisibleTo grants RepoPulse.UnitTests access).
    [Fact]
    public void NoDataFactory_ProducesCanonicalState()
    {
        var result = CommitFrequencyTrendScore.NoData();

        Assert.Null(result.Value);
        Assert.Equal(CommitFrequencyTrendBand.NoData, result.Band);
    }

    [Fact]
    public void InconsistentDataFactory_ProducesCanonicalState()
    {
        var result = CommitFrequencyTrendScore.InconsistentData();

        Assert.Null(result.Value);
        Assert.Equal(CommitFrequencyTrendBand.InconsistentData, result.Band);
    }

    [Fact]
    public void StableInactiveFactory_ProducesCanonicalState()
    {
        var result = CommitFrequencyTrendScore.StableInactive();

        Assert.Equal(0, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.StableInactive, result.Band);
    }

    [Fact]
    public void AcceleratingFactory_ProducesCanonicalState()
    {
        var result = CommitFrequencyTrendScore.Accelerating();

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Accelerating, result.Band);
    }

    [Fact]
    public void StableFactory_ProducesCanonicalState()
    {
        var result = CommitFrequencyTrendScore.Stable();

        Assert.Equal(60, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Stable, result.Band);
    }

    [Fact]
    public void DeceleratingFactory_ProducesCanonicalState()
    {
        var result = CommitFrequencyTrendScore.Decelerating();

        Assert.Equal(25, result.Value);
        Assert.Equal(CommitFrequencyTrendBand.Decelerating, result.Band);
    }

    // 36. No IGitHubApiClient/HttpClient/session/token-shaped member anywhere
    // on the score or scorer types.
    [Fact]
    public void ScoreAndScorer_HaveNoApiClientSessionOrTokenShapedMember()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Client", "Secret", "Http", "Repository" };

        var scoreMembers = typeof(CommitFrequencyTrendScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
        var scorerMembers = typeof(CommitFrequencyTrendScorer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(f => f.Name)
            .Concat(typeof(CommitFrequencyTrendScorer).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name));

        foreach (var name in scoreMembers.Concat(scorerMembers))
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // 37. Regression: RP-015's ActivityRecencyScorer suite is unaffected —
    // covered by the shared full-assembly test run (see
    // ActivityRecencyScorerTests.cs).

    // 38. Regression: RP-016's commit-count tests are unaffected — covered
    // by the shared full-assembly test run (see GitHubApiClientTests.cs's
    // GetDefaultBranchCommitCountAsync section).

    // 39. Regression: RP-017's commit-frequency tests are unaffected —
    // covered by the shared full-assembly test run (see
    // CommitFrequencyScorerTests.cs).

    // 40. Regression: the full existing suite passes — verified by running
    // the whole RepoPulse.UnitTests assembly, not a dedicated test here.

    private static string GetScorerSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(
            testDirectory, "..", "..", "src", "RepoPulse.Core", "Scoring", "CommitFrequencyTrendScorer.cs"));
    }
}
