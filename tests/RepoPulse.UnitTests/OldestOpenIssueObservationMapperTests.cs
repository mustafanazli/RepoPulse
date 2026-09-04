using System.Reflection;
using System.Runtime.CompilerServices;
using RepoPulse.Core.Analysis;
using RepoPulse.Core.Repositories;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-022: the adaptation seam turning RP-020's GitHubOldestOpenIssueResult
// into RP-021's OldestOpenIssueObservation. These tests exercise the
// documented conversion table, the "every failure collapses to NoData"
// rule, the null/invariant behaviour, and the layering constraints that
// keep this mapper free of network, scoring and clock responsibilities
// (see its doc comment and RepoPulse-Project-Plan.md's RP-022 entry).
// No GitHub API call, no MAUI, no SQLite, no system clock.
public class OldestOpenIssueObservationMapperTests
{
    // A fixed, arbitrary reference instant. Nothing in the mapper depends on
    // the calendar date; this only keeps the tests readable.
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------
    // A. Successful conversions
    // ---------------------------------------------------------------

    [Fact]
    public void SuccessWithOpenIssue_MapsToFoundObservation()
    {
        var result = GitHubOldestOpenIssueResult.Success(CreatedAt);

        var observation = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(OldestOpenIssueObservationKind.Found, observation.Kind);
        Assert.NotNull(observation.CreatedAtUtc);
    }

    [Fact]
    public void SuccessWithOpenIssue_PreservesTheExactInstant()
    {
        var result = GitHubOldestOpenIssueResult.Success(CreatedAt);

        var observation = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(CreatedAt.UtcTicks, observation.CreatedAtUtc!.Value.UtcTicks);
    }

    [Fact]
    public void SuccessWithOpenIssue_ProducesUtcOffsetZero()
    {
        var result = GitHubOldestOpenIssueResult.Success(CreatedAt);

        var observation = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(TimeSpan.Zero, observation.CreatedAtUtc!.Value.Offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-5)]
    [InlineData(9)]
    [InlineData(-11)]
    [InlineData(14)]
    public void SuccessWithAnyInputOffset_PreservesTheInstantAndNormalizesToUtc(int offsetHours)
    {
        // The same absolute instant, expressed with a different offset.
        var shifted = CreatedAt.ToOffset(TimeSpan.FromHours(offsetHours));
        var result = GitHubOldestOpenIssueResult.Success(shifted);

        var observation = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(TimeSpan.Zero, observation.CreatedAtUtc!.Value.Offset);
        Assert.Equal(CreatedAt.UtcTicks, observation.CreatedAtUtc!.Value.UtcTicks);
    }

    [Fact]
    public void SuccessWithNoOpenIssues_MapsToNoOpenIssuesObservation()
    {
        var result = GitHubOldestOpenIssueResult.NoOpenIssues();

        var observation = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(OldestOpenIssueObservationKind.NoOpenIssues, observation.Kind);
        Assert.Null(observation.CreatedAtUtc);
    }

    // ---------------------------------------------------------------
    // B. Every failure collapses to NoData
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(GitHubOldestOpenIssueFailureKind.RepositoryUnavailable)]
    [InlineData(GitHubOldestOpenIssueFailureKind.Unauthorized)]
    [InlineData(GitHubOldestOpenIssueFailureKind.RateLimited)]
    [InlineData(GitHubOldestOpenIssueFailureKind.NetworkError)]
    [InlineData(GitHubOldestOpenIssueFailureKind.Unexpected)]
    public void EachNamedFailureKind_MapsToNoData(GitHubOldestOpenIssueFailureKind failureKind)
    {
        var result = GitHubOldestOpenIssueResult.Failure(failureKind);

        var observation = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(OldestOpenIssueObservationKind.NoData, observation.Kind);
        Assert.Null(observation.CreatedAtUtc);
    }

    [Fact]
    public void EveryDeclaredFailureKind_MapsToNoData()
    {
        // Enumerated rather than listed, so a failure kind added to the enum
        // later is covered by this test the moment it is declared.
        var kinds = Enum.GetValues<GitHubOldestOpenIssueFailureKind>();
        Assert.NotEmpty(kinds);

        foreach (var kind in kinds)
        {
            var observation = OldestOpenIssueObservationMapper.Map(
                GitHubOldestOpenIssueResult.Failure(kind));

            Assert.Equal(OldestOpenIssueObservationKind.NoData, observation.Kind);
        }
    }

    [Fact]
    public void FailureIsNeverMappedToNoOpenIssues()
    {
        // The distinction this whole model exists to protect: an unanswered
        // query must never become the positive claim "the backlog is empty",
        // which the scorer reads as a perfect 100/Clear.
        foreach (var kind in Enum.GetValues<GitHubOldestOpenIssueFailureKind>())
        {
            var observation = OldestOpenIssueObservationMapper.Map(
                GitHubOldestOpenIssueResult.Failure(kind));

            Assert.NotEqual(OldestOpenIssueObservationKind.NoOpenIssues, observation.Kind);
        }
    }

    [Fact]
    public void FailureIsNeverMappedToFound()
    {
        foreach (var kind in Enum.GetValues<GitHubOldestOpenIssueFailureKind>())
        {
            var observation = OldestOpenIssueObservationMapper.Map(
                GitHubOldestOpenIssueResult.Failure(kind));

            Assert.NotEqual(OldestOpenIssueObservationKind.Found, observation.Kind);
            Assert.Null(observation.CreatedAtUtc);
        }
    }

    [Fact]
    public void NoOpenIssuesIsNeverMappedToFound()
    {
        var observation = OldestOpenIssueObservationMapper.Map(
            GitHubOldestOpenIssueResult.NoOpenIssues());

        Assert.NotEqual(OldestOpenIssueObservationKind.Found, observation.Kind);
        Assert.Null(observation.CreatedAtUtc);
    }

    // ---------------------------------------------------------------
    // C. RepositoryUnavailable is not an applicability decision
    // ---------------------------------------------------------------

    [Fact]
    public void RepositoryUnavailable_MapsToNoDataNotAnApplicabilityDecision()
    {
        var observation = OldestOpenIssueObservationMapper.Map(
            GitHubOldestOpenIssueResult.Failure(
                GitHubOldestOpenIssueFailureKind.RepositoryUnavailable));

        Assert.Equal(OldestOpenIssueObservationKind.NoData, observation.Kind);
    }

    [Fact]
    public void ObservationModel_HasNoNotApplicableState_SoTheMapperCannotExpressOne()
    {
        // Structural proof rather than a behavioural one: NotApplicable is a
        // band the SCORER produces from isArchived/isFork, and there is no
        // observation kind that could carry it out of this mapper.
        var kinds = Enum.GetNames<OldestOpenIssueObservationKind>();

        Assert.DoesNotContain("NotApplicable", kinds);
        Assert.Equal(3, kinds.Length);
    }

    [Fact]
    public void RepositoryUnavailable_ScoresAsNoDataForANormalRepository()
    {
        // End to end through the real scorer: an access failure on a repo
        // that is neither archived nor a fork must not surface as
        // NotApplicable, and must not surface as a number.
        var observation = OldestOpenIssueObservationMapper.Map(
            GitHubOldestOpenIssueResult.Failure(
                GitHubOldestOpenIssueFailureKind.RepositoryUnavailable));

        var score = OldestOpenIssueAgeScorer.Score(
            observation, CreatedAt, isArchived: false, isFork: false);

        Assert.Equal(OldestOpenIssueAgeBand.NoData, score.Band);
        Assert.Null(score.Value);
    }

    // ---------------------------------------------------------------
    // D. Null and invariant behaviour
    // ---------------------------------------------------------------

    [Fact]
    public void NullResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => OldestOpenIssueObservationMapper.Map(null!));
    }

    [Fact]
    public void NullResult_ThrowsWithTheParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OldestOpenIssueObservationMapper.Map(null!));

        Assert.Equal("result", exception.ParamName);
    }

    [Fact]
    public void NullResult_IsNotSilentlyTreatedAsNoData()
    {
        // A null argument is a caller bug, not an outcome. If it were
        // absorbed into NoData, a broken call site would be indistinguishable
        // from a repository whose issue query merely failed.
        var thrown = Record.Exception(() => OldestOpenIssueObservationMapper.Map(null!));

        Assert.NotNull(thrown);
        Assert.IsType<ArgumentNullException>(thrown);
    }

    [Fact]
    public void MappedObservation_CarriesNoReferenceToTheResultOrItsFailureKind()
    {
        var observation = OldestOpenIssueObservationMapper.Map(
            GitHubOldestOpenIssueResult.Failure(
                GitHubOldestOpenIssueFailureKind.RateLimited));

        var propertyTypes = observation.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueResult), propertyTypes);
        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueFailureKind), propertyTypes);
        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueFailureKind?), propertyTypes);

        var fieldTypes = observation.GetType()
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueResult), fieldTypes);
        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueFailureKind), fieldTypes);
        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueFailureKind?), fieldTypes);
    }

    [Fact]
    public void Map_DoesNotMutateTheResultItWasGiven()
    {
        var result = GitHubOldestOpenIssueResult.Success(CreatedAt);

        _ = OldestOpenIssueObservationMapper.Map(result);

        Assert.True(result.IsSuccess);
        Assert.True(result.HasOpenIssues);
        Assert.Equal(CreatedAt.UtcTicks, result.CreatedAtUtc!.Value.UtcTicks);
        Assert.Null(result.FailureKind);
    }

    [Fact]
    public void Map_IsDeterministic_TheSameResultProducesAnEquivalentObservation()
    {
        var result = GitHubOldestOpenIssueResult.Success(CreatedAt);

        var first = OldestOpenIssueObservationMapper.Map(result);
        var second = OldestOpenIssueObservationMapper.Map(result);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Map_IsDeterministicForEveryOutcomeShape()
    {
        AssertStable(GitHubOldestOpenIssueResult.Success(CreatedAt));
        AssertStable(GitHubOldestOpenIssueResult.NoOpenIssues());

        foreach (var kind in Enum.GetValues<GitHubOldestOpenIssueFailureKind>())
        {
            AssertStable(GitHubOldestOpenIssueResult.Failure(kind));
        }

        static void AssertStable(GitHubOldestOpenIssueResult result) =>
            Assert.Equal(
                OldestOpenIssueObservationMapper.Map(result),
                OldestOpenIssueObservationMapper.Map(result));
    }

    // ---------------------------------------------------------------
    // E. Purity and layering
    // ---------------------------------------------------------------

    [Fact]
    public void Mapper_IsAStaticClass()
    {
        var type = typeof(OldestOpenIssueObservationMapper);

        // A C# static class compiles to abstract + sealed.
        Assert.True(type.IsAbstract);
        Assert.True(type.IsSealed);
    }

    [Fact]
    public void Mapper_HasNoAccessibleInstanceConstructor()
    {
        var constructors = typeof(OldestOpenIssueObservationMapper).GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    [Fact]
    public void Mapper_HasNoMutableStaticState()
    {
        var mutableFields = typeof(OldestOpenIssueObservationMapper)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .Select(field => field.Name)
            .ToArray();

        Assert.Empty(mutableFields);
    }

    [Fact]
    public void Mapper_PublicSurfaceIsExactlyTheMapMethod()
    {
        var publicMethods = typeof(OldestOpenIssueObservationMapper)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(new[] { "Map" }, publicMethods);
    }

    [Fact]
    public void Map_ReturnsExactlyAnOldestOpenIssueObservation()
    {
        var method = typeof(OldestOpenIssueObservationMapper).GetMethod("Map")!;

        Assert.Equal(typeof(OldestOpenIssueObservation), method.ReturnType);
    }

    [Fact]
    public void Map_TakesOnlyTheResult_NoClockArchivedForkOrCancellationToken()
    {
        var parameters = typeof(OldestOpenIssueObservationMapper)
            .GetMethod("Map")!
            .GetParameters();

        var parameter = Assert.Single(parameters);
        Assert.Equal(typeof(GitHubOldestOpenIssueResult), parameter.ParameterType);
        Assert.Equal("result", parameter.Name);
    }

    [Fact]
    public void Map_IsSynchronous()
    {
        var method = typeof(OldestOpenIssueObservationMapper).GetMethod("Map")!;

        Assert.False(typeof(System.Threading.Tasks.Task).IsAssignableFrom(method.ReturnType));
        Assert.Null(method.GetCustomAttribute<AsyncStateMachineAttribute>());
    }

    [Fact]
    public void Mapper_ExposesNoTokenSessionOrTransportMembers()
    {
        string[] forbidden =
        [
            "token", "session", "credential", "header", "url", "uri",
            "body", "message", "http", "client", "cache", "logger", "retry"
        ];

        var memberNames = typeof(OldestOpenIssueObservationMapper)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.Name.ToLowerInvariant())
            .ToArray();

        foreach (var name in memberNames)
        {
            Assert.DoesNotContain(forbidden, fragment => name.Contains(fragment));
        }
    }

    [Fact]
    public void Mapper_ReferencesOnlyTheTwoDomainTypesItBridges()
    {
        var method = typeof(OldestOpenIssueObservationMapper).GetMethod("Map")!;

        var signatureTypes = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType)
            .ToArray();

        Assert.Equal(
            new[] { typeof(GitHubOldestOpenIssueResult), typeof(OldestOpenIssueObservation) },
            signatureTypes);
    }

    // ---------------------------------------------------------------
    // F. Source scans — no clock, no network, no async, no scoring
    // ---------------------------------------------------------------

    [Fact]
    public void MapperSource_ReadsNoSystemClock()
    {
        var code = MapperCodeWithoutComments();

        Assert.DoesNotContain("DateTime.Now", code);
        Assert.DoesNotContain("DateTime.UtcNow", code);
        Assert.DoesNotContain("DateTime.Today", code);
        Assert.DoesNotContain("DateTimeOffset.Now", code);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", code);
        Assert.DoesNotContain("TimeProvider", code);
        Assert.DoesNotContain("Stopwatch", code);
        Assert.DoesNotContain("Environment.TickCount", code);
    }

    [Fact]
    public void MapperSource_HasNoNetworkOrTransportDependency()
    {
        var code = MapperCodeWithoutComments();

        Assert.DoesNotContain("HttpClient", code);
        Assert.DoesNotContain("HttpRequestMessage", code);
        Assert.DoesNotContain("HttpResponseMessage", code);
        Assert.DoesNotContain("IGitHubApiClient", code);
        Assert.DoesNotContain("GitHubApiClient", code);
        Assert.DoesNotContain("SQLite", code);
        Assert.DoesNotContain("Microsoft.Maui", code);
    }

    [Fact]
    public void MapperSource_IsSynchronousAndCancellationFree()
    {
        var code = MapperCodeWithoutComments();

        Assert.DoesNotContain("async", code);
        Assert.DoesNotContain("await", code);
        Assert.DoesNotContain("Task<", code);
        Assert.DoesNotContain("CancellationToken", code);
    }

    [Fact]
    public void MapperSource_ProducesNoScoreAndSwitchesOnNoFailureKind()
    {
        var code = MapperCodeWithoutComments();

        // Scoring belongs to OldestOpenIssueAgeScorer; the mapper must not
        // reach for a band, a value or the scorer itself.
        Assert.DoesNotContain("OldestOpenIssueAgeScore", code);
        Assert.DoesNotContain("OldestOpenIssueAgeScorer", code);
        Assert.DoesNotContain("OldestOpenIssueAgeBand", code);

        // Every failure collapses to NoData precisely because the mapper
        // never inspects which failure it was.
        Assert.DoesNotContain("FailureKind", code);
        Assert.DoesNotContain("RepositoryUnavailable", code);
        Assert.DoesNotContain("Unauthorized", code);
        Assert.DoesNotContain("RateLimited", code);
    }

    [Fact]
    public void MapperSource_DoesNotRoundTripTheTimestampThroughText()
    {
        var code = MapperCodeWithoutComments();

        Assert.DoesNotContain("ToString(", code);
        Assert.DoesNotContain("Parse(", code);
        Assert.DoesNotContain("ToLocalTime", code);
    }

    [Fact]
    public void MapperSourceScan_ActuallyReadsTheProductionFile()
    {
        // Guards the scans above from silently passing on an empty string:
        // if the path ever stopped resolving, File.ReadAllLines would throw,
        // and this assertion pins that the real type is what was read.
        var code = MapperCodeWithoutComments();

        Assert.Contains("class OldestOpenIssueObservationMapper", code);
        Assert.Contains("ArgumentNullException.ThrowIfNull(result)", code);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static string MapperCodeWithoutComments()
    {
        var source = File.ReadAllLines(GetMapperSourcePath());
        return string.Join('\n', source.Where(line => !line.TrimStart().StartsWith("//")));
    }

    private static string GetMapperSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(
            testDirectory, "..", "..", "src", "RepoPulse.Core", "Analysis",
            "OldestOpenIssueObservationMapper.cs"));
    }
}
