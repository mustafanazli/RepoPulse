using System.Reflection;
using System.Runtime.CompilerServices;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-015: Faz 2 / issue #13's smallest vertical slice — only the
// "son commit güncelliği" component of the Aktivite sub-score. These tests
// exercise ActivityRecencyScorer's own documented threshold table (see its
// doc comment and RepoPulse-Project-Plan.md's RP-015 entry) with exact
// TimeSpan boundaries — no approximation or rounding anywhere.
public class ActivityRecencyScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NullLastCommit_ScoresZeroAndNoCommitsBand()
    {
        var result = ActivityRecencyScorer.Score(null, Now);

        Assert.Equal(0, result.Value);
        Assert.Equal(ActivityRecencyBand.NoCommits, result.Band);
    }

    [Fact]
    public void SameInstantAsNow_ScoresFresh100()
    {
        var result = ActivityRecencyScorer.Score(Now, Now);

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityRecencyBand.Fresh, result.Band);
    }

    [Fact]
    public void OneDayAgo_ScoresFresh100()
    {
        var result = ActivityRecencyScorer.Score(Now.AddDays(-1), Now);

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityRecencyBand.Fresh, result.Band);
    }

    [Fact]
    public void ExactlySevenDaysAgo_ScoresFresh100()
    {
        var lastCommitUtc = Now - TimeSpan.FromDays(7);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityRecencyBand.Fresh, result.Band);
    }

    [Fact]
    public void SevenDaysAndOneTickAgo_ScoresRecent75()
    {
        var lastCommitUtc = Now - TimeSpan.FromDays(7) - TimeSpan.FromTicks(1);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(75, result.Value);
        Assert.Equal(ActivityRecencyBand.Recent, result.Band);
    }

    [Fact]
    public void ExactlyThirtyDaysAgo_ScoresRecent75()
    {
        var lastCommitUtc = Now - TimeSpan.FromDays(30);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(75, result.Value);
        Assert.Equal(ActivityRecencyBand.Recent, result.Band);
    }

    [Fact]
    public void ThirtyDaysAndOneTickAgo_ScoresAging40()
    {
        var lastCommitUtc = Now - TimeSpan.FromDays(30) - TimeSpan.FromTicks(1);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(40, result.Value);
        Assert.Equal(ActivityRecencyBand.Aging, result.Band);
    }

    [Fact]
    public void ExactlyNinetyDaysAgo_ScoresAging40()
    {
        var lastCommitUtc = Now - TimeSpan.FromDays(90);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(40, result.Value);
        Assert.Equal(ActivityRecencyBand.Aging, result.Band);
    }

    [Fact]
    public void NinetyDaysAndOneTickAgo_ScoresStale10()
    {
        var lastCommitUtc = Now - TimeSpan.FromDays(90) - TimeSpan.FromTicks(1);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(10, result.Value);
        Assert.Equal(ActivityRecencyBand.Stale, result.Band);
    }

    [Fact]
    public void VeryOldCommit_ScoresStale10()
    {
        var lastCommitUtc = Now.AddYears(-5);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(10, result.Value);
        Assert.Equal(ActivityRecencyBand.Stale, result.Band);
    }

    [Fact]
    public void FutureCommit_ClampsToZeroAgeAndScoresFresh100()
    {
        // Git commit metadata is not controlled by the local clock — a
        // misconfigured clock can make a real commit appear to postdate
        // "now". This must never throw and must never be treated as
        // evidence of staleness.
        var lastCommitUtc = Now.AddDays(5);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(100, result.Value);
        Assert.Equal(ActivityRecencyBand.Fresh, result.Band);
    }

    [Fact]
    public void SameUtcInstantWithDifferentOffsets_ScoresIdentically()
    {
        var instantUtc = Now.AddDays(-10);
        var sameInstantDifferentOffset = instantUtc.ToOffset(TimeSpan.FromHours(5));

        // Genuinely the same point in time, just displayed with a different
        // local offset — DateTimeOffset subtraction is offset-independent.
        Assert.NotEqual(instantUtc.Offset, sameInstantDifferentOffset.Offset);
        Assert.Equal(instantUtc, sameInstantDifferentOffset);

        var resultFromUtc = ActivityRecencyScorer.Score(instantUtc, Now);
        var resultFromOffset = ActivityRecencyScorer.Score(sameInstantDifferentOffset, Now);

        Assert.Equal(resultFromUtc, resultFromOffset);
    }

    [Fact]
    public void SameInputs_ProduceDeterministicResult()
    {
        var lastCommitUtc = Now.AddDays(-45);

        var first = ActivityRecencyScorer.Score(lastCommitUtc, Now);
        var second = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-9999)] // far future (negative days-ago == future)
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(9999)] // far past
    public void Value_IsAlwaysWithinZeroToOneHundred(int? daysAgo)
    {
        DateTimeOffset? lastCommitUtc = daysAgo is null ? null : Now.AddDays(-daysAgo.Value);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.InRange(result.Value, 0, 100);
    }

    [Fact]
    public void AlgorithmVersion_IsExactlyZeroDotOneDotZero()
    {
        Assert.Equal("0.1.0", ActivityRecencyScorer.AlgorithmVersion);

        var result = ActivityRecencyScorer.Score(Now, Now);
        Assert.Equal("0.1.0", result.AlgorithmVersion);
    }

    [Theory]
    [InlineData(null, 0, ActivityRecencyBand.NoCommits)]
    [InlineData(0, 100, ActivityRecencyBand.Fresh)]
    [InlineData(15, 75, ActivityRecencyBand.Recent)]
    [InlineData(60, 40, ActivityRecencyBand.Aging)]
    [InlineData(200, 10, ActivityRecencyBand.Stale)]
    public void BandAlwaysMatchesItsDocumentedValue(int? daysAgo, int expectedValue, ActivityRecencyBand expectedBand)
    {
        DateTimeOffset? lastCommitUtc = daysAgo is null ? null : Now.AddDays(-daysAgo.Value);

        var result = ActivityRecencyScorer.Score(lastCommitUtc, Now);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedBand, result.Band);
    }

    [Fact]
    public void Scorer_NeverReadsSystemClockDirectly()
    {
        // "gerçekçi" structural proof, not just review — reads the actual
        // shipped source and strips comment lines (which legitimately
        // *mention* DateTime.Now/UtcNow while documenting why they must
        // never be called) before asserting the real code never calls them.
        var source = File.ReadAllLines(GetScorerSourcePath());
        var codeOnly = string.Join('\n', source.Where(line => !line.TrimStart().StartsWith("//")));

        Assert.DoesNotContain("DateTime.Now", codeOnly);
        Assert.DoesNotContain("DateTimeOffset.Now", codeOnly);
        Assert.DoesNotContain(".UtcNow", codeOnly);
    }

    [Fact]
    public void NullContractIsDocumentedInSource()
    {
        var source = File.ReadAllText(GetScorerSourcePath());

        Assert.Contains("NULL SEMANTICS", source);
        Assert.Contains("must NEVER be used to", source);
        Assert.Contains("data unavailable", source);
    }

    [Fact]
    public void ScoreAndScorer_HaveNoTokenSessionOrApiClientShapedMember()
    {
        var forbiddenSubstrings = new[] { "Token", "Session", "Client", "Secret", "Verifier", "Code" };

        var scoreMembers = typeof(ActivityRecencyScore)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
        var scorerFields = typeof(ActivityRecencyScorer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(f => f.Name);

        foreach (var name in scoreMembers.Concat(scorerFields))
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string GetScorerSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(
            testDirectory, "..", "..", "src", "RepoPulse.Core", "Scoring", "ActivityRecencyScorer.cs"));
    }
}
