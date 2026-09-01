using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-019: Faz 2 / issue #13's Aktivite sub-score composition — combines
// RP-015's ActivityRecencyScore, RP-017's CommitFrequencyScore, and RP-018's
// CommitFrequencyTrendScore into one deterministic result via
// ActivityScorer.Score. These tests exercise the documented required-data
// policy, the Full/partial weighted-average math (long-based, round-half-up,
// no floating point), the band table, and the invariant-factory design (see
// ActivityScorer's and ActivityScore's doc comments and
// RepoPulse-Project-Plan.md's RP-019 entry).
public class ActivityScorerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // --- Component builders (each named after the Value it produces) ---

    private static ActivityRecencyScore NoCommitsRecency() => ActivityRecencyScorer.Score(null, NowUtc); // 0
    private static ActivityRecencyScore StaleRecency() => ActivityRecencyScorer.Score(NowUtc.AddDays(-100), NowUtc); // 10
    private static ActivityRecencyScore AgingRecency() => ActivityRecencyScorer.Score(NowUtc.AddDays(-60), NowUtc); // 40
    private static ActivityRecencyScore RecentRecency() => ActivityRecencyScorer.Score(NowUtc.AddDays(-15), NowUtc); // 75
    private static ActivityRecencyScore FreshRecency() => ActivityRecencyScorer.Score(NowUtc.AddDays(-1), NowUtc); // 100

    private static CommitFrequencyScore NoDataFrequency() => CommitFrequencyScorer.Score(null);
    private static CommitFrequencyScore InactiveFrequency() => CommitFrequencyScorer.Score(0); // 0
    private static CommitFrequencyScore LowFrequency() => CommitFrequencyScorer.Score(1); // 40
    private static CommitFrequencyScore ModerateFrequency() => CommitFrequencyScorer.Score(10); // 70
    private static CommitFrequencyScore HighFrequency() => CommitFrequencyScorer.Score(20); // 100

    private static CommitFrequencyTrendScore NoDataTrend() => CommitFrequencyTrendScorer.Score(null, 10);
    private static CommitFrequencyTrendScore InconsistentTrend() => CommitFrequencyTrendScorer.Score(5, 4);
    private static CommitFrequencyTrendScore StableInactiveTrend() => CommitFrequencyTrendScorer.Score(0, 0); // 0
    private static CommitFrequencyTrendScore DeceleratingTrend() => CommitFrequencyTrendScorer.Score(0, 10); // 25
    private static CommitFrequencyTrendScore StableTrend() => CommitFrequencyTrendScorer.Score(10, 30); // 60
    private static CommitFrequencyTrendScore AcceleratingTrend() => CommitFrequencyTrendScorer.Score(1, 1); // 100

    // ===================== Required-data (1-7) =====================

    // 1. recency null + frequency NoData -> NoData/MissingBothRequired,
    // regardless of what trend is.
    [Fact]
    public void RecencyNullAndFrequencyNoData_ScoresNoDataWithMissingBothRequired()
    {
        var result = ActivityScorer.Score(null, NoDataFrequency(), StableTrend());

        Assert.Null(result.Value);
        Assert.Equal(ActivityScoreBand.NoData, result.Band);
        Assert.Equal(ActivityScoreCompleteness.MissingBothRequired, result.Completeness);
    }

    // 2. recency null + frequency valid -> NoData/MissingRequiredRecency.
    [Fact]
    public void RecencyNullWithValidFrequency_ScoresNoDataWithMissingRequiredRecency()
    {
        var result = ActivityScorer.Score(null, ModerateFrequency(), StableTrend());

        Assert.Null(result.Value);
        Assert.Equal(ActivityScoreBand.NoData, result.Band);
        Assert.Equal(ActivityScoreCompleteness.MissingRequiredRecency, result.Completeness);
    }

    // 3. recency valid + frequency NoData -> NoData/MissingRequiredFrequency.
    [Fact]
    public void ValidRecencyWithFrequencyNoData_ScoresNoDataWithMissingRequiredFrequency()
    {
        var result = ActivityScorer.Score(RecentRecency(), NoDataFrequency(), StableTrend());

        Assert.Null(result.Value);
        Assert.Equal(ActivityScoreBand.NoData, result.Band);
        Assert.Equal(ActivityScoreCompleteness.MissingRequiredFrequency, result.Completeness);
    }

    // 4. Confirmed NoCommits recency (Value=0, non-null instance) is NOT
    // missing data -> produces a real Scored result.
    [Fact]
    public void ConfirmedNoCommitsRecency_IsNotTreatedAsMissing()
    {
        var result = ActivityScorer.Score(NoCommitsRecency(), ModerateFrequency(), StableTrend());

        Assert.Equal(37, result.Value);
        Assert.NotEqual(ActivityScoreBand.NoData, result.Band);
        Assert.Equal(ActivityScoreCompleteness.Full, result.Completeness);
    }

    // 5. Frequency Inactive (Value=0) is NOT treated as missing data ->
    // produces a real Scored result.
    [Fact]
    public void FrequencyInactive_IsNotTreatedAsMissing()
    {
        var result = ActivityScorer.Score(RecentRecency(), InactiveFrequency(), StableTrend());

        Assert.Equal(46, result.Value);
        Assert.NotEqual(ActivityScoreBand.NoData, result.Band);
        Assert.Equal(ActivityScoreCompleteness.Full, result.Completeness);
    }

    // 6. frequencyScore itself null -> ArgumentNullException.
    [Fact]
    public void NullFrequencyScore_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ActivityScorer.Score(RecentRecency(), null!, StableTrend()));

        Assert.Equal("frequencyScore", exception.ParamName);
    }

    // 7. trendScore itself null -> ArgumentNullException.
    [Fact]
    public void NullTrendScore_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ActivityScorer.Score(RecentRecency(), ModerateFrequency(), null!));

        Assert.Equal("trendScore", exception.ParamName);
    }

    // ===================== Full calculation (8-13) =====================

    // 8. 100/100/100 -> 100 HighlyActive Full.
    [Fact]
    public void AllComponentsMaximum_ScoresOneHundredHighlyActiveFull()
    {
        var result = ActivityScorer.Score(FreshRecency(), HighFrequency(), AcceleratingTrend());

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityScoreBand.HighlyActive, result.Band);
        Assert.Equal(ActivityScoreCompleteness.Full, result.Completeness);
    }

    // 9. 0/0/0 -> 0 Dormant Full.
    [Fact]
    public void AllComponentsMinimum_ScoresZeroDormantFull()
    {
        var result = ActivityScorer.Score(NoCommitsRecency(), InactiveFrequency(), StableInactiveTrend());

        Assert.Equal(0, result.Value);
        Assert.Equal(ActivityScoreBand.Dormant, result.Band);
        Assert.Equal(ActivityScoreCompleteness.Full, result.Completeness);
    }

    // 10. Representative mixed values -> exact hand-computed result.
    // weightedSum = 75*45 + 70*35 + 60*20 = 3375+2450+1200 = 7025
    // combined = (7025+50)/100 = 70 (70.75 truncated).
    [Fact]
    public void MixedRepresentativeValues_ProducesExactExpectedResult()
    {
        var result = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());

        Assert.Equal(70, result.Value);
        Assert.Equal(ActivityScoreBand.Active, result.Band);
        Assert.Equal(ActivityScoreCompleteness.Full, result.Completeness);
    }

    // 11. Full denominator is exactly 100.
    [Fact]
    public void FullDenominator_IsExactlyOneHundred()
    {
        Assert.Equal(100, ActivityScorer.TotalWeight);
        Assert.Equal(100, ActivityScorer.RecencyWeight + ActivityScorer.FrequencyWeight + ActivityScorer.TrendWeight);
    }

    // 12. Full round-half-up at an exact .5 boundary.
    // weightedSum = 10*45 + 40*35 + 0*20 = 450+1400 = 1850 (exactly 18.5*100)
    // combined = (1850+50)/100 = 19 (rounds UP from .5, not down/to-even).
    [Fact]
    public void FullCalculation_ExactHalfBoundary_RoundsUp()
    {
        var result = ActivityScorer.Score(StaleRecency(), LowFrequency(), StableInactiveTrend());

        Assert.Equal(19, result.Value);
    }

    // 13. Trend StableInactive (Value=0) is a valid FULL input, not a
    // missing/partial one.
    [Fact]
    public void TrendStableInactive_CountsAsFullNotPartial()
    {
        var result = ActivityScorer.Score(FreshRecency(), HighFrequency(), StableInactiveTrend());

        Assert.Equal(80, result.Value);
        Assert.Equal(ActivityScoreCompleteness.Full, result.Completeness);
    }

    // ===================== Partial trend (14-22) =====================

    // 14. Trend NoData -> PartialTrendNoData.
    // availableWeightedSum = 75*45 + 70*35 = 3375+2450 = 5825
    // combined = (5825+40)/80 = 73 (73.3125 truncated).
    [Fact]
    public void TrendNoData_ScoresPartialTrendNoData()
    {
        var result = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), NoDataTrend());

        Assert.Equal(73, result.Value);
        Assert.Equal(ActivityScoreCompleteness.PartialTrendNoData, result.Completeness);
    }

    // 15. Trend InconsistentData -> PartialTrendInconsistent, same math as #14.
    [Fact]
    public void TrendInconsistentData_ScoresPartialTrendInconsistent()
    {
        var result = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), InconsistentTrend());

        Assert.Equal(73, result.Value);
        Assert.Equal(ActivityScoreCompleteness.PartialTrendInconsistent, result.Completeness);
    }

    // 16. Partial denominator is exactly 80 (Recency+Frequency weights).
    [Fact]
    public void PartialDenominator_IsExactlyEighty()
    {
        Assert.Equal(80, ActivityScorer.RecencyWeight + ActivityScorer.FrequencyWeight);
    }

    // 17. Recency/Frequency weights stay their original 45/35 in the partial path.
    [Fact]
    public void PartialPath_KeepsOriginalRecencyAndFrequencyWeights()
    {
        Assert.Equal(45, ActivityScorer.RecencyWeight);
        Assert.Equal(35, ActivityScorer.FrequencyWeight);
    }

    // 18. No approximate integer "effective weight" (e.g. 56/44) literal is
    // ever used in the scorer's actual code — only the exact 45/35/80
    // formulation. Source-scanned with comment lines stripped, since the
    // doc comment explains the equivalent 56.25%/43.75% percentages in prose.
    [Fact]
    public void Scorer_NeverUsesApproximateEffectiveWeightLiterals()
    {
        var lines = File.ReadAllLines(GetScorerSourcePath());
        var codeOnly = string.Join('\n', lines.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("56", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("44", codeOnly, StringComparison.Ordinal);
    }

    // 19. Partial round-half-up at an exact .5 boundary.
    // availableWeightedSum = 0*45 + 40*35 = 1400 (exactly 17.5*80)
    // combined = (1400+40)/80 = 18 (rounds UP from .5).
    [Fact]
    public void PartialCalculation_ExactHalfBoundary_RoundsUp()
    {
        var result = ActivityScorer.Score(NoCommitsRecency(), LowFrequency(), NoDataTrend());

        Assert.Equal(18, result.Value);
    }

    // 20. Trend missing, recency/frequency both maximum -> 100.
    [Fact]
    public void TrendMissingWithMaximumRecencyAndFrequency_ScoresOneHundred()
    {
        var result = ActivityScorer.Score(FreshRecency(), HighFrequency(), NoDataTrend());

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityScoreCompleteness.PartialTrendNoData, result.Completeness);
    }

    // 21. Trend missing, recency/frequency both minimum -> 0.
    [Fact]
    public void TrendMissingWithMinimumRecencyAndFrequency_ScoresZero()
    {
        var result = ActivityScorer.Score(NoCommitsRecency(), InactiveFrequency(), NoDataTrend());

        Assert.Equal(0, result.Value);
        Assert.Equal(ActivityScoreCompleteness.PartialTrendNoData, result.Completeness);
    }

    // 22. Trend's missing weight is NOT silently counted as 0 over the full
    // 100 denominator — the 80 denominator is used instead, producing a
    // measurably different (correct) result: 56, not the 45 a wrong
    // 100-denominator/trend=0 calculation would produce.
    [Fact]
    public void TrendMissing_DoesNotCountAsZeroOverFullDenominator()
    {
        var result = ActivityScorer.Score(FreshRecency(), InactiveFrequency(), NoDataTrend());

        Assert.Equal(56, result.Value);
    }

    // ===================== Band boundaries (23-32) =====================

    [Fact]
    public void BandBoundary_ZeroIsDormant()
    {
        var result = ActivityScorer.Score(NoCommitsRecency(), InactiveFrequency(), StableInactiveTrend());

        Assert.Equal(0, result.Value);
        Assert.Equal(ActivityScoreBand.Dormant, result.Band);
    }

    [Fact]
    public void BandBoundary_NineteenIsDormant()
    {
        var result = ActivityScorer.Score(StaleRecency(), LowFrequency(), StableInactiveTrend());

        Assert.Equal(19, result.Value);
        Assert.Equal(ActivityScoreBand.Dormant, result.Band);
    }

    [Fact]
    public void BandBoundary_TwentyIsLow()
    {
        var result = ActivityScorer.Score(NoCommitsRecency(), InactiveFrequency(), AcceleratingTrend());

        Assert.Equal(20, result.Value);
        Assert.Equal(ActivityScoreBand.Low, result.Band);
    }

    [Fact]
    public void BandBoundary_ThirtyNineIsLow()
    {
        var result = ActivityScorer.Score(RecentRecency(), InactiveFrequency(), DeceleratingTrend());

        Assert.Equal(39, result.Value);
        Assert.Equal(ActivityScoreBand.Low, result.Band);
    }

    [Fact]
    public void BandBoundary_FortyIsModerate()
    {
        var result = ActivityScorer.Score(StaleRecency(), HighFrequency(), StableInactiveTrend());

        Assert.Equal(40, result.Value);
        Assert.Equal(ActivityScoreBand.Moderate, result.Band);
    }

    [Fact]
    public void BandBoundary_FiftyNineIsModerate()
    {
        var result = ActivityScorer.Score(FreshRecency(), LowFrequency(), StableInactiveTrend());

        Assert.Equal(59, result.Value);
        Assert.Equal(ActivityScoreBand.Moderate, result.Band);
    }

    [Fact]
    public void BandBoundary_SixtyIsActive()
    {
        var result = ActivityScorer.Score(StaleRecency(), HighFrequency(), AcceleratingTrend());

        Assert.Equal(60, result.Value);
        Assert.Equal(ActivityScoreBand.Active, result.Band);
    }

    [Fact]
    public void BandBoundary_SeventyNineIsActive()
    {
        var result = ActivityScorer.Score(FreshRecency(), LowFrequency(), AcceleratingTrend());

        Assert.Equal(79, result.Value);
        Assert.Equal(ActivityScoreBand.Active, result.Band);
    }

    [Fact]
    public void BandBoundary_EightyIsHighlyActive()
    {
        var result = ActivityScorer.Score(FreshRecency(), HighFrequency(), StableInactiveTrend());

        Assert.Equal(80, result.Value);
        Assert.Equal(ActivityScoreBand.HighlyActive, result.Band);
    }

    [Fact]
    public void BandBoundary_OneHundredIsHighlyActive()
    {
        var result = ActivityScorer.Score(FreshRecency(), HighFrequency(), AcceleratingTrend());

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityScoreBand.HighlyActive, result.Band);
    }

    // ===================== Invariant (33-41) =====================

    // 33. NoData factory rejects Scored-only completeness reasons.
    [Theory]
    [InlineData(ActivityScoreCompleteness.Full)]
    [InlineData(ActivityScoreCompleteness.PartialTrendNoData)]
    [InlineData(ActivityScoreCompleteness.PartialTrendInconsistent)]
    public void NoDataFactory_RejectsScoredCompletenessReasons(ActivityScoreCompleteness completeness)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityScore.NoData(completeness));
    }

    // 34. Scored factory rejects missing-required completeness reasons.
    [Theory]
    [InlineData(ActivityScoreCompleteness.MissingRequiredRecency)]
    [InlineData(ActivityScoreCompleteness.MissingRequiredFrequency)]
    [InlineData(ActivityScoreCompleteness.MissingBothRequired)]
    public void ScoredFactory_RejectsMissingRequiredCompletenessReasons(ActivityScoreCompleteness completeness)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityScore.Scored(50, completeness));
    }

    // 35. Scored factory rejects out-of-range values.
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ScoredFactory_RejectsOutOfRangeValues(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityScore.Scored(value, ActivityScoreCompleteness.Full));
    }

    // 36. NoData's Value is always null, for every missing-required reason.
    [Theory]
    [InlineData(ActivityScoreCompleteness.MissingRequiredRecency)]
    [InlineData(ActivityScoreCompleteness.MissingRequiredFrequency)]
    [InlineData(ActivityScoreCompleteness.MissingBothRequired)]
    public void NoDataFactory_ValueIsAlwaysNull(ActivityScoreCompleteness completeness)
    {
        var result = ActivityScore.NoData(completeness);

        Assert.Null(result.Value);
        Assert.Equal(ActivityScoreBand.NoData, result.Band);
    }

    // 37. Caller cannot set Band/version/weight — no public property setters exist.
    [Fact]
    public void ActivityScore_HasNoPublicPropertySetters()
    {
        var properties = typeof(ActivityScore).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    // 38. No public constructor and no generic "Create" factory.
    [Fact]
    public void ActivityScore_HasNoPublicConstructorOrGenericCreateFactory()
    {
        var publicConstructors = typeof(ActivityScore).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var createMethods = typeof(ActivityScore)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "Create");

        Assert.Empty(publicConstructors);
        Assert.Empty(createMethods);
    }

    // 39. Result is an immutable record: sealed, and two calls with
    // structurally identical inputs produce equal results (value semantics).
    [Fact]
    public void ActivityScore_IsSealedImmutableRecordWithValueEquality()
    {
        Assert.True(typeof(ActivityScore).IsSealed);

        var first = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());
        var second = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());

        Assert.Equal(first, second);
    }

    // 40. Component AlgorithmVersions on the result match each sub-scorer's
    // own constant exactly.
    [Fact]
    public void Result_CarriesCorrectComponentAlgorithmVersions()
    {
        var result = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());

        Assert.Equal(ActivityRecencyScorer.AlgorithmVersion, result.RecencyAlgorithmVersion);
        Assert.Equal(CommitFrequencyScorer.AlgorithmVersion, result.FrequencyAlgorithmVersion);
        Assert.Equal(CommitFrequencyTrendScorer.AlgorithmVersion, result.TrendAlgorithmVersion);
    }

    // 41. Activity's own AlgorithmVersion is exactly "0.1.0".
    [Fact]
    public void ActivityAlgorithmVersion_IsExactlyZeroDotOneDotZero()
    {
        Assert.Equal("0.1.0", ActivityScorer.AlgorithmVersion);

        var result = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());
        Assert.Equal("0.1.0", result.AlgorithmVersion);
    }

    // 41a. ComponentId is exactly "activity" (RP-019 metadata; matches the
    // ComponentId pattern already used by CommitFrequencyScorer and
    // CommitFrequencyTrendScorer — added after the PR #21 pre-merge audit
    // found ActivityScorer was the only RP-019-touched scorer missing it).
    [Fact]
    public void ComponentId_IsExactlyActivity()
    {
        Assert.Equal("activity", ActivityScorer.ComponentId);
    }

    // 41b. ComponentId is a compile-time constant (const string field) — not
    // a caller-settable static property, so there is no way to fabricate a
    // different component id at runtime.
    [Fact]
    public void ComponentId_IsConstStaticStringField()
    {
        var field = typeof(ActivityScorer).GetField(nameof(ActivityScorer.ComponentId), BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal(typeof(string), field.FieldType);
        Assert.True(field.IsLiteral);
    }

    // 41c. ActivityScore itself carries no ComponentId property — the
    // identifier lives solely on ActivityScorer, exactly like AlgorithmVersion
    // does (ActivityScore.AlgorithmVersion is copied FROM
    // ActivityScorer.AlgorithmVersion inside the private constructor), but no
    // such copy, constructor parameter, or factory parameter exists for
    // ComponentId.
    [Fact]
    public void ActivityScore_HasNoComponentIdProperty()
    {
        var properties = typeof(ActivityScore).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(properties, p => p.Name == "ComponentId");
    }

    // ===================== Overflow/determinism (42-46) =====================

    // 42. All weight constants used in the arithmetic are long, not int.
    [Theory]
    [InlineData("RecencyWeight")]
    [InlineData("FrequencyWeight")]
    [InlineData("TrendWeight")]
    [InlineData("TotalWeight")]
    public void WeightConstants_AreLong(string fieldName)
    {
        var field = typeof(ActivityScorer).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal(typeof(long), field.FieldType);
    }

    // 43. Same inputs -> same (equal) result, deterministically.
    [Fact]
    public void SameInputs_ProduceDeterministicResult()
    {
        var recency = RecentRecency();
        var frequency = ModerateFrequency();
        var trend = StableTrend();

        var first = ActivityScorer.Score(recency, frequency, trend);
        var second = ActivityScorer.Score(recency, frequency, trend);

        Assert.Equal(first, second);
    }

    // 44. Result is identical regardless of CurrentCulture.
    [Fact]
    public void DifferentCurrentCulture_ScoresIdentically()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turkish = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = ActivityScorer.Score(RecentRecency(), ModerateFrequency(), StableTrend());

            Assert.Equal(invariant, turkish);
            Assert.Equal(invariant, german);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // 45. ActivityScorer never reads the system clock directly.
    [Fact]
    public void Scorer_NeverReadsSystemClockDirectly()
    {
        var source = File.ReadAllText(GetScorerSourcePath());
        var codeOnly = string.Join('\n', source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("DateTime.Now", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.Now", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", codeOnly, StringComparison.Ordinal);
    }

    // 46. Neither result nor scorer type carries an API/session/token/client
    // shaped member.
    [Fact]
    public void ScoreAndScorer_HaveNoApiClientSessionOrTokenShapedMember()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Client", "Secret", "Http", "Repository" };

        var scoreMembers = typeof(ActivityScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
        var scorerMembers = typeof(ActivityScorer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(f => f.Name)
            .Concat(typeof(ActivityScorer).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name));

        foreach (var name in scoreMembers.Concat(scorerMembers))
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ===================== Regression (47-50) =====================

    // 47. Regression: RP-015's ActivityRecencyScorer suite is unaffected —
    // covered by the shared full-assembly test run (see
    // ActivityRecencyScorerTests.cs).

    // 48. Regression: RP-017's CommitFrequencyScorer suite is unaffected —
    // covered by the shared full-assembly test run (see
    // CommitFrequencyScorerTests.cs).

    // 49. Regression: RP-018's CommitFrequencyTrendScorer suite is
    // unaffected — covered by the shared full-assembly test run (see
    // CommitFrequencyTrendScorerTests.cs).

    // 50. Regression: the full existing suite passes — verified by running
    // the whole RepoPulse.UnitTests assembly, not a dedicated test here.

    private static string GetScorerSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(
            testDirectory, "..", "..", "src", "RepoPulse.Core", "Scoring", "ActivityScorer.cs"));
    }
}
