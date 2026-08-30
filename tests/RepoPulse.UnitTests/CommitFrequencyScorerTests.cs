using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-017: Faz 2 / issue #13's next smallest vertical slice — only the
// "30 günlük commit sıklığı" component of the Aktivite sub-score. These
// tests exercise CommitFrequencyScorer's own documented threshold table
// (see its doc comment and RepoPulse-Project-Plan.md's RP-017 entry) with
// exact integer comparisons — no rounding or ratio math anywhere.
public class CommitFrequencyScorerTests
{
    // 1. null → Value null + NoData.
    [Fact]
    public void NullCommitCount_ScoresNullValueAndNoDataBand()
    {
        var result = CommitFrequencyScorer.Score(null);

        Assert.Null(result.Value);
        Assert.Equal(CommitFrequencyBand.NoData, result.Band);
    }

    // 2. null is not the same as 0 — distinct bands.
    [Fact]
    public void NullCommitCount_IsDistinctFromZeroCommitCount()
    {
        var noData = CommitFrequencyScorer.Score(null);
        var zero = CommitFrequencyScorer.Score(0);

        Assert.NotEqual(noData.Band, zero.Band);
        Assert.Null(noData.Value);
        Assert.Equal(0, zero.Value);
    }

    // 3. 0 → 0 + Inactive.
    [Fact]
    public void ZeroCommitCount_ScoresZeroAndInactiveBand()
    {
        var result = CommitFrequencyScorer.Score(0);

        Assert.Equal(0, result.Value);
        Assert.Equal(CommitFrequencyBand.Inactive, result.Band);
    }

    // 4. 1 → 40 + Low.
    [Fact]
    public void OneCommit_ScoresFortyAndLowBand()
    {
        var result = CommitFrequencyScorer.Score(1);

        Assert.Equal(40, result.Value);
        Assert.Equal(CommitFrequencyBand.Low, result.Band);
    }

    // 5. 4 → 40 + Low (upper boundary of Low).
    [Fact]
    public void FourCommits_ScoresFortyAndLowBand()
    {
        var result = CommitFrequencyScorer.Score(4);

        Assert.Equal(40, result.Value);
        Assert.Equal(CommitFrequencyBand.Low, result.Band);
    }

    // 6. 5 → 70 + Moderate (lower boundary of Moderate).
    [Fact]
    public void FiveCommits_ScoresSeventyAndModerateBand()
    {
        var result = CommitFrequencyScorer.Score(5);

        Assert.Equal(70, result.Value);
        Assert.Equal(CommitFrequencyBand.Moderate, result.Band);
    }

    // 7. 14 → 70 + Moderate (upper boundary of Moderate).
    [Fact]
    public void FourteenCommits_ScoresSeventyAndModerateBand()
    {
        var result = CommitFrequencyScorer.Score(14);

        Assert.Equal(70, result.Value);
        Assert.Equal(CommitFrequencyBand.Moderate, result.Band);
    }

    // 8. 15 → 100 + High (lower boundary of High).
    [Fact]
    public void FifteenCommits_ScoresOneHundredAndHighBand()
    {
        var result = CommitFrequencyScorer.Score(15);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyBand.High, result.Band);
    }

    // 9. int.MaxValue → 100 + High, without overflow.
    [Fact]
    public void IntMaxValueCommits_ScoresOneHundredAndHighBandWithoutOverflow()
    {
        var result = CommitFrequencyScorer.Score(int.MaxValue);

        Assert.Equal(100, result.Value);
        Assert.Equal(CommitFrequencyBand.High, result.Band);
    }

    // 10. -1 → controlled rejection, never silently clamped.
    [Fact]
    public void NegativeOneCommit_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitFrequencyScorer.Score(-1));
    }

    // 11. int.MinValue → controlled rejection.
    [Fact]
    public void IntMinValueCommits_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitFrequencyScorer.Score(int.MinValue));
    }

    // 12. Same input produces a deterministic result.
    [Fact]
    public void SameInput_ProducesDeterministicResult()
    {
        var first = CommitFrequencyScorer.Score(7);
        var second = CommitFrequencyScorer.Score(7);

        Assert.Equal(first, second);
    }

    // 13. AlgorithmVersion is exactly "0.1.0".
    [Fact]
    public void AlgorithmVersion_IsExactlyZeroDotOneDotZero()
    {
        Assert.Equal("0.1.0", CommitFrequencyScorer.AlgorithmVersion);

        var result = CommitFrequencyScorer.Score(5);
        Assert.Equal("0.1.0", result.AlgorithmVersion);
    }

    // 14. ComponentId is exactly "commit-frequency".
    [Fact]
    public void ComponentId_IsExactlyCommitFrequency()
    {
        Assert.Equal("commit-frequency", CommitFrequencyScorer.ComponentId);
    }

    // 15. WindowDays is exactly 30, and every result carries it.
    [Fact]
    public void WindowDays_IsExactlyThirty()
    {
        Assert.Equal(30, CommitFrequencyScorer.WindowDays);

        var result = CommitFrequencyScorer.Score(5);
        Assert.Equal(30, result.WindowDays);
    }

    // 16. NoData's Value is null.
    [Fact]
    public void NoDataBand_HasNullValue()
    {
        var result = CommitFrequencyScorer.Score(null);

        Assert.Equal(CommitFrequencyBand.NoData, result.Band);
        Assert.Null(result.Value);
    }

    // 17. Every scored (non-NoData) result's Value stays within 0-100.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(int.MaxValue)]
    public void Value_IsAlwaysWithinZeroToOneHundred(int commitCount)
    {
        var result = CommitFrequencyScorer.Score(commitCount);

        Assert.NotNull(result.Value);
        Assert.InRange(result.Value!.Value, 0, 100);
    }

    // 18. Band always matches its documented Value.
    [Theory]
    [InlineData(null, null, CommitFrequencyBand.NoData)]
    [InlineData(0, 0, CommitFrequencyBand.Inactive)]
    [InlineData(2, 40, CommitFrequencyBand.Low)]
    [InlineData(9, 70, CommitFrequencyBand.Moderate)]
    [InlineData(30, 100, CommitFrequencyBand.High)]
    public void BandAlwaysMatchesItsDocumentedValue(int? commitCount, int? expectedValue, CommitFrequencyBand expectedBand)
    {
        var result = CommitFrequencyScorer.Score(commitCount);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedBand, result.Band);
    }

    // 19. ObservedCommitCount matches the input across every band, and is
    // null exactly when (and only when) the band is NoData.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(int.MaxValue)]
    public void ObservedCommitCount_MatchesInputAcrossAllBands(int? commitCount)
    {
        var result = CommitFrequencyScorer.Score(commitCount);

        Assert.Equal(commitCount, result.ObservedCommitCount);
        Assert.Equal(commitCount is null, result.Band == CommitFrequencyBand.NoData);
    }

    // 20. CommitFrequencyScore has no public constructor.
    [Fact]
    public void CommitFrequencyScore_HasNoPublicConstructor()
    {
        var constructors = typeof(CommitFrequencyScore).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(constructors);
    }

    // 21. No public static factory on CommitFrequencyScore itself can produce
    // an instance — the only route to a CommitFrequencyScore is
    // CommitFrequencyScorer.Score(int?), which always builds a consistent
    // combination.
    [Fact]
    public void CommitFrequencyScore_HasNoPublicFactoryOfItsOwn()
    {
        var type = typeof(CommitFrequencyScore);
        var publicStaticFactories = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == type);

        Assert.Empty(publicStaticFactories);
    }

    // 22. CommitFrequencyScorer never reads the system clock.
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

    // 23. No IGitHubApiClient/HttpClient/session/token-shaped member anywhere
    // on the score or scorer types.
    [Fact]
    public void ScoreAndScorer_HaveNoApiClientSessionOrTokenShapedMember()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Client", "Secret", "Http", "Repository" };

        var scoreMembers = typeof(CommitFrequencyScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
        var scorerMembers = typeof(CommitFrequencyScorer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(f => f.Name)
            .Concat(typeof(CommitFrequencyScorer).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name));

        foreach (var name in scoreMembers.Concat(scorerMembers))
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // 24. A different current culture must not affect the result — integer
    // comparisons carry no culture-sensitive formatting or parsing.
    [Fact]
    public void DifferentCurrentCulture_ScoresIdentically()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantResult = CommitFrequencyScorer.Score(9);

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turkishResult = CommitFrequencyScorer.Score(9);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var germanResult = CommitFrequencyScorer.Score(9);

            Assert.Equal(invariantResult, turkishResult);
            Assert.Equal(invariantResult, germanResult);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // 25. Regression: RP-015's ActivityRecencyScorer suite is unaffected —
    // covered by the shared full-assembly test run, no dedicated test
    // needed here beyond that shared run (see ActivityRecencyScorerTests.cs).

    // 26. Regression: RP-016's commit-count tests are unaffected — covered
    // by the shared full-assembly test run (see GitHubApiClientTests.cs's
    // GetDefaultBranchCommitCountAsync section).

    // 27. Regression: the full existing suite passes — verified by running
    // the whole RepoPulse.UnitTests assembly, not a dedicated test here.

    private static string GetScorerSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(
            testDirectory, "..", "..", "src", "RepoPulse.Core", "Scoring", "CommitFrequencyScorer.cs"));
    }
}
