using System.Globalization;
using System.Reflection;
using RepoPulse.Core.Analysis;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;
using RepoPulse.Core.Scoring;

namespace RepoPulse.UnitTests;

// RP-023: OldestOpenIssueAnalyzer — the orchestration slice that finally
// connects RP-020's GraphQL query, RP-022's observation mapping and
// RP-021's age scoring, and that keeps the operational outcome of a run
// (AnalysisSignalStatus) strictly separate from the score it produced.
//
// Every test here uses a fake IGitHubApiClient. Nothing in this file makes
// an HTTP request, touches the network, or reads the system clock: the
// analysis timestamp is always supplied explicitly so the expected band is
// a fact of the input, not of when the suite happens to run.
public sealed class OldestOpenIssueAnalyzerTests
{
    private const string Token = "gho_test_token";
    private const string Owner = "octocat";
    private const string Name = "hello-world";

    private static readonly DateTimeOffset AnalysisAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------
    // A. Behaviour table: successful runs
    // ---------------------------------------------------------------

    // 1-4. A found issue is scored by real age, and the run reports
    // Succeeded with exactly one client call.
    [Theory]
    [InlineData(10, 100, OldestOpenIssueAgeBand.Fresh)]
    [InlineData(60, 75, OldestOpenIssueAgeBand.Aging)]
    [InlineData(120, 40, OldestOpenIssueAgeBand.Stale)]
    [InlineData(400, 10, OldestOpenIssueAgeBand.SeverelyStale)]
    public async Task FoundIssue_ScoresByAge_AndReportsSucceeded(int ageInDays, int expectedValue, OldestOpenIssueAgeBand expectedBand)
    {
        var client = FakeGitHubApiClient.Returning(
            GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-ageInDays)));
        var analyzer = new OldestOpenIssueAnalyzer(client);

        var result = await analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.Succeeded, result.Status);
        Assert.Equal(expectedValue, result.Score.Value);
        Assert.Equal(expectedBand, result.Score.Band);
        Assert.Equal(1, client.OldestOpenIssueCallCount);
    }

    // 5. A positively confirmed empty backlog is a real, successful answer —
    // Clear/100, never NoData and never a failure status.
    [Fact]
    public async Task NoOpenIssues_ReportsSucceededWithClearScore()
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());
        var analyzer = new OldestOpenIssueAnalyzer(client);

        var result = await analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.Succeeded, result.Status);
        Assert.Equal(100, result.Score.Value);
        Assert.Equal(OldestOpenIssueAgeBand.Clear, result.Score.Band);
        Assert.Equal(1, client.OldestOpenIssueCallCount);
    }

    // ---------------------------------------------------------------
    // B. Behaviour table: failures — reason preserved in Status only
    // ---------------------------------------------------------------

    // 6-10. Each RP-020 failure kind becomes its own operational status
    // while the score stays NoData/null, exactly as the mapper and scorer
    // require. One client call is still made.
    [Theory]
    [InlineData(GitHubOldestOpenIssueFailureKind.RepositoryUnavailable, AnalysisSignalStatus.RepositoryUnavailable)]
    [InlineData(GitHubOldestOpenIssueFailureKind.Unauthorized, AnalysisSignalStatus.Unauthorized)]
    [InlineData(GitHubOldestOpenIssueFailureKind.RateLimited, AnalysisSignalStatus.RateLimited)]
    [InlineData(GitHubOldestOpenIssueFailureKind.NetworkError, AnalysisSignalStatus.NetworkUnavailable)]
    [InlineData(GitHubOldestOpenIssueFailureKind.Unexpected, AnalysisSignalStatus.Failed)]
    public async Task Failure_PreservesReasonInStatusAndScoresNoData(
        GitHubOldestOpenIssueFailureKind failureKind,
        AnalysisSignalStatus expectedStatus)
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Failure(failureKind));
        var analyzer = new OldestOpenIssueAnalyzer(client);

        var result = await analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Score.Value);
        Assert.Equal(OldestOpenIssueAgeBand.NoData, result.Score.Band);
        Assert.Equal(1, client.OldestOpenIssueCallCount);
    }

    // 11. Every failure kind RP-020 can produce — including any added after
    // this test was written — always scores NoData and never claims a
    // succeeded or skipped status.
    [Fact]
    public async Task EveryFailureKind_ScoresNoDataAndNeverClaimsSuccessOrSkip()
    {
        foreach (var failureKind in Enum.GetValues<GitHubOldestOpenIssueFailureKind>())
        {
            var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Failure(failureKind));
            var analyzer = new OldestOpenIssueAnalyzer(client);

            var result = await analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

            Assert.Equal(OldestOpenIssueAgeBand.NoData, result.Score.Band);
            Assert.Null(result.Score.Value);
            Assert.NotEqual(AnalysisSignalStatus.Succeeded, result.Status);
            Assert.NotEqual(AnalysisSignalStatus.NotApplicableSkipped, result.Status);
            Assert.True(Enum.IsDefined(result.Status));
        }
    }

    // 12. The failure-kind translation loses nothing: the five kinds map to
    // five DISTINCT statuses, so a caller can still tell "sign the user out"
    // apart from "retry later" apart from "we cannot see this repository".
    [Fact]
    public async Task FailureKinds_MapToDistinctStatuses()
    {
        var statuses = new List<AnalysisSignalStatus>();

        foreach (var failureKind in Enum.GetValues<GitHubOldestOpenIssueFailureKind>())
        {
            var analyzer = new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Failure(failureKind)));

            var result = await analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);
            statuses.Add(result.Status);
        }

        Assert.Equal(statuses.Count, statuses.Distinct().Count());
    }

    // ---------------------------------------------------------------
    // C. Applicability short-circuit: archived / fork
    // ---------------------------------------------------------------

    // 13-15. Archived and/or fork repositories are NotApplicableSkipped and
    // make no client call at all.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ArchivedOrFork_SkipsRequestEntirely(bool isArchived, bool isFork)
    {
        var client = FakeGitHubApiClient.Returning(
            GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-1)));
        var analyzer = new OldestOpenIssueAnalyzer(client);

        var result = await analyzer.AnalyzeAsync(
            Token,
            RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
            AnalysisAt,
            CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.NotApplicableSkipped, result.Status);
        Assert.Null(result.Score.Value);
        Assert.Equal(OldestOpenIssueAgeBand.NotApplicable, result.Score.Band);
        Assert.Equal(0, client.OldestOpenIssueCallCount);
    }

    // 16-18. The strongest form of the same proof: a client that throws on
    // ANY member. If the analyzer touched it, these would fail loudly.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ArchivedOrFork_NeverTouchesTheApiClient(bool isArchived, bool isFork)
    {
        var analyzer = new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient());

        var result = await analyzer.AnalyzeAsync(
            Token,
            RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
            AnalysisAt,
            CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.NotApplicableSkipped, result.Status);
    }

    // 19. The same underlying data produces genuinely different, correct
    // outcomes depending only on applicability — a fresh issue on a normal
    // repository is Fresh/Succeeded, on an archived one NotApplicable/skipped.
    [Fact]
    public async Task SameData_ScoresDifferentlyForNormalAndArchivedRepository()
    {
        var apiResult = GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-3));

        var normal = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
            .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        var archived = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
            .AnalyzeAsync(Token, RepositoryAnalysisContext.Create(Owner, Name, isArchived: true, isFork: false), AnalysisAt, CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.Succeeded, normal.Status);
        Assert.Equal(OldestOpenIssueAgeBand.Fresh, normal.Score.Band);
        Assert.Equal(100, normal.Score.Value);

        Assert.Equal(AnalysisSignalStatus.NotApplicableSkipped, archived.Status);
        Assert.Equal(OldestOpenIssueAgeBand.NotApplicable, archived.Score.Band);
        Assert.Null(archived.Score.Value);
    }

    // 20. NotApplicableSkipped is not a failure: it never shares a status
    // with any failing run.
    [Fact]
    public async Task NotApplicableSkipped_IsDistinctFromEveryFailureStatus()
    {
        var skipped = await new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient())
            .AnalyzeAsync(Token, RepositoryAnalysisContext.Create(Owner, Name, isArchived: true, isFork: false), AnalysisAt, CancellationToken.None);

        foreach (var failureKind in Enum.GetValues<GitHubOldestOpenIssueFailureKind>())
        {
            var failed = await new OldestOpenIssueAnalyzer(
                    FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Failure(failureKind)))
                .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

            Assert.NotEqual(failed.Status, skipped.Status);
            Assert.NotEqual(failed.Score.Band, skipped.Score.Band);
        }
    }

    // ---------------------------------------------------------------
    // D. Cancellation
    // ---------------------------------------------------------------

    // 21. An already-cancelled run on a normal repository throws and makes
    // no request.
    [Fact]
    public async Task PreCancelledToken_NormalRepository_ThrowsAndMakesNoRequest()
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());
        var analyzer = new OldestOpenIssueAnalyzer(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, cts.Token));

        Assert.Equal(0, client.OldestOpenIssueCallCount);
    }

    // 22. An already-cancelled run stays cancelled even for an archived or
    // fork repository — the applicability short-circuit must not manufacture
    // a NotApplicableSkipped "answer" for a run nobody is waiting on.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PreCancelledToken_ArchivedOrFork_StillThrows(bool isArchived, bool isFork)
    {
        var analyzer = new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync(
                Token,
                RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
                AnalysisAt,
                cts.Token));
    }

    // 23. A cancellation raised by the client propagates untouched — it is
    // never converted into a failure status, because an abandoned run is not
    // a failed measurement.
    [Fact]
    public async Task ClientCancellation_PropagatesAndIsNeverAFailureStatus()
    {
        using var cts = new CancellationTokenSource();
        var analyzer = new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Throwing(
            () => new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, cts.Token));
    }

    // 24. The caller's cancellation token reaches the client unchanged — the
    // analyzer never substitutes a token of its own.
    [Fact]
    public async Task CallerCancellationToken_IsForwardedToTheClient()
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());
        var analyzer = new OldestOpenIssueAnalyzer(client);
        using var cts = new CancellationTokenSource();

        _ = await analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, cts.Token);

        Assert.Equal(cts.Token, Assert.Single(client.Calls).CancellationToken);
    }

    // 25. A defect surfacing as an exception is not disguised as a plausible
    // GitHub failure — the analyzer has no broad catch.
    [Fact]
    public async Task ClientException_IsNotSwallowedIntoAFailedStatus()
    {
        var analyzer = new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Throwing(
            () => new InvalidOperationException("defect")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => analyzer.AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None));
    }

    // ---------------------------------------------------------------
    // E. Argument guards
    // ---------------------------------------------------------------

    // 26. A null client cannot be injected.
    [Fact]
    public void Constructor_RejectsNullClient()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OldestOpenIssueAnalyzer(null!));

        Assert.Equal("gitHubApiClient", exception.ParamName);
    }

    // 27-28. Null token and null repository are rejected with the right
    // parameter names.
    [Fact]
    public async Task AnalyzeAsync_RejectsNullAccessToken()
    {
        var analyzer = new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => analyzer.AnalyzeAsync(null!, NormalRepository(), AnalysisAt, CancellationToken.None));

        Assert.Equal("accessToken", exception.ParamName);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsNullRepository()
    {
        var analyzer = new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => analyzer.AnalyzeAsync(Token, null!, AnalysisAt, CancellationToken.None));

        Assert.Equal("repository", exception.ParamName);
    }

    // 29. The applicability short-circuit must not mask a guard: a null
    // token still throws for an archived repository, even though that path
    // never uses the token.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NullAccessToken_StillThrowsForArchivedOrFork(bool isArchived, bool isFork)
    {
        var analyzer = new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => analyzer.AnalyzeAsync(
                null!,
                RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
                AnalysisAt,
                CancellationToken.None));

        Assert.Equal("accessToken", exception.ParamName);
    }

    // 30. Argument guards run before the cancellation check, so a malformed
    // call is reported as malformed rather than silently swallowed by an
    // already-cancelled token.
    [Fact]
    public async Task NullArgument_IsReportedEvenWhenTokenIsAlreadyCancelled()
    {
        var analyzer = new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => analyzer.AnalyzeAsync(null!, NormalRepository(), AnalysisAt, cts.Token));
    }

    // ---------------------------------------------------------------
    // F. RepositoryAnalysisContext invariants
    // ---------------------------------------------------------------

    // 31-32. Null owner/name are rejected with the right parameter name.
    [Fact]
    public void Context_RejectsNullOwner()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => RepositoryAnalysisContext.Create(null!, Name, isArchived: false, isFork: false));

        Assert.Equal("owner", exception.ParamName);
    }

    [Fact]
    public void Context_RejectsNullName()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => RepositoryAnalysisContext.Create(Owner, null!, isArchived: false, isFork: false));

        Assert.Equal("name", exception.ParamName);
    }

    // 33-34. Empty and whitespace identities are rejected as ArgumentException.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   \r\n ")]
    public void Context_RejectsEmptyOrWhitespaceOwner(string owner)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => RepositoryAnalysisContext.Create(owner, Name, isArchived: false, isFork: false));

        Assert.Equal("owner", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   \r\n ")]
    public void Context_RejectsEmptyOrWhitespaceName(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => RepositoryAnalysisContext.Create(Owner, name, isArchived: false, isFork: false));

        Assert.Equal("name", exception.ParamName);
    }

    // 35. Identity validation is NOT masked by applicability: an archived or
    // fork repository with a malformed identity still fails at construction,
    // so a bad identity can never ride along unnoticed on the skip path.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Context_ValidatesIdentityEvenForArchivedOrFork(bool isArchived, bool isFork)
    {
        Assert.Throws<ArgumentNullException>(
            () => RepositoryAnalysisContext.Create(null!, Name, isArchived, isFork));
        Assert.Throws<ArgumentException>(
            () => RepositoryAnalysisContext.Create("  ", Name, isArchived, isFork));
        Assert.Throws<ArgumentNullException>(
            () => RepositoryAnalysisContext.Create(Owner, null!, isArchived, isFork));
        Assert.Throws<ArgumentException>(
            () => RepositoryAnalysisContext.Create(Owner, "  ", isArchived, isFork));
    }

    // 36. A valid identity is stored exactly as supplied — never trimmed,
    // re-cased or otherwise rewritten, so the analyzer can never query a
    // repository other than the one it was asked about.
    [Fact]
    public void Context_PreservesIdentityExactly()
    {
        var context = RepositoryAnalysisContext.Create(" Oct.Cat ", " Hello-World ", isArchived: false, isFork: true);

        Assert.Equal(" Oct.Cat ", context.Owner);
        Assert.Equal(" Hello-World ", context.Name);
        Assert.False(context.IsArchived);
        Assert.True(context.IsFork);
    }

    // 37. The identity reaches the client verbatim.
    [Fact]
    public async Task Analyzer_ForwardsOwnerAndNameVerbatim()
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());
        var analyzer = new OldestOpenIssueAnalyzer(client);

        _ = await analyzer.AnalyzeAsync(
            Token,
            RepositoryAnalysisContext.Create("Owner.With-Dots", "Repo_Name.1", isArchived: false, isFork: false),
            AnalysisAt,
            CancellationToken.None);

        var call = Assert.Single(client.Calls);
        Assert.Equal("Owner.With-Dots", call.Owner);
        Assert.Equal("Repo_Name.1", call.Repository);
        Assert.Equal(Token, call.AccessToken);
    }

    // 38. Construction is closed: no public constructor, no settable or
    // init-settable property, and no generic Create escape hatch.
    [Fact]
    public void Context_HasNoPublicConstructorSetterOrGenericCreate()
    {
        var type = typeof(RepositoryAnalysisContext);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Assert.Null(p.SetMethod));

        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.ReturnType == type)
            .ToList();

        var factory = Assert.Single(factories);
        Assert.Equal("Create", factory.Name);
        Assert.Equal(4, factory.GetParameters().Length);
    }

    // 39. The context carries nothing token/session/transport shaped, and in
    // particular no access token.
    [Fact]
    public void Context_CarriesNoTokenOrSessionMember()
    {
        AssertNoForbiddenMemberNames(
            typeof(RepositoryAnalysisContext),
            "Token", "Session", "Secret", "Credential", "Password", "Http", "Url", "Header", "Body", "Client");
    }

    // 40. Scope assertion: the context deliberately carries only what this
    // slice uses. DefaultBranch is not speculatively included.
    [Fact]
    public void Context_ExposesOnlyTheFourMembersThisSliceUses()
    {
        var names = typeof(RepositoryAnalysisContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .Order()
            .ToArray();

        Assert.Equal(new[] { "IsArchived", "IsFork", "Name", "Owner" }, names);
    }

    // ---------------------------------------------------------------
    // G. Time: caller ownership, normalization, determinism
    // ---------------------------------------------------------------

    // 41. The caller's analysis timestamp is echoed back, normalized to UTC.
    [Fact]
    public async Task AnalysisTimestamp_IsEchoedBackNormalizedToUtc()
    {
        var analyzer = new OldestOpenIssueAnalyzer(
            FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues()));
        var localTimestamp = new DateTimeOffset(2026, 9, 6, 15, 0, 0, TimeSpan.FromHours(3));

        var result = await analyzer.AnalyzeAsync(Token, NormalRepository(), localTimestamp, CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, result.AnalysisTimestampUtc.Offset);
        Assert.Equal(localTimestamp.UtcDateTime, result.AnalysisTimestampUtc.UtcDateTime);
    }

    // 42. All three result shapes normalize the timestamp, not just the
    // successful one.
    [Fact]
    public async Task AnalysisTimestamp_IsNormalizedForEveryOutcome()
    {
        var localTimestamp = new DateTimeOffset(2026, 9, 6, 15, 0, 0, TimeSpan.FromHours(3));

        var succeeded = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues()))
            .AnalyzeAsync(Token, NormalRepository(), localTimestamp, CancellationToken.None);

        var skipped = await new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient())
            .AnalyzeAsync(Token, RepositoryAnalysisContext.Create(Owner, Name, isArchived: true, isFork: false), localTimestamp, CancellationToken.None);

        var failed = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Failure(GitHubOldestOpenIssueFailureKind.RateLimited)))
            .AnalyzeAsync(Token, NormalRepository(), localTimestamp, CancellationToken.None);

        foreach (var result in new[] { succeeded, skipped, failed })
        {
            Assert.Equal(TimeSpan.Zero, result.AnalysisTimestampUtc.Offset);
            Assert.Equal(localTimestamp.UtcDateTime, result.AnalysisTimestampUtc.UtcDateTime);
        }
    }

    // 43. The same instant expressed with different offsets produces
    // equivalent results — the analyzer compares instants, not clock faces.
    [Fact]
    public async Task SameInstantWithDifferentOffsets_ProducesEquivalentResults()
    {
        var createdAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var utc = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var shifted = utc.ToOffset(TimeSpan.FromHours(-7));

        var first = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Success(createdAt)))
            .AnalyzeAsync(Token, NormalRepository(), utc, CancellationToken.None);

        var second = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Success(createdAt)))
            .AnalyzeAsync(Token, NormalRepository(), shifted, CancellationToken.None);

        Assert.Equal(first, second);
    }

    // 44. The caller's exact instant reaches the scorer: a run one tick past
    // the 30-day boundary bands differently from one exactly on it. Only an
    // unmodified, untruncated timestamp can produce this.
    [Fact]
    public async Task ExactCallerInstant_ReachesTheScorerUntruncated()
    {
        var createdAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var onBoundary = createdAt.AddDays(30);
        var justPast = onBoundary.AddTicks(1);

        var atBoundary = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Success(createdAt)))
            .AnalyzeAsync(Token, NormalRepository(), onBoundary, CancellationToken.None);

        var pastBoundary = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Success(createdAt)))
            .AnalyzeAsync(Token, NormalRepository(), justPast, CancellationToken.None);

        Assert.Equal(OldestOpenIssueAgeBand.Fresh, atBoundary.Score.Band);
        Assert.Equal(OldestOpenIssueAgeBand.Aging, pastBoundary.Score.Band);
    }

    // 45. No system clock: a timestamp deep in the past still bands by the
    // supplied instant, which would be impossible if the analyzer read the
    // real clock.
    [Fact]
    public async Task Analyzer_UsesTheSuppliedInstantNotTheSystemClock()
    {
        var createdAt = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var analysisAt = createdAt.AddDays(5);

        var result = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Success(createdAt)))
            .AnalyzeAsync(Token, NormalRepository(), analysisAt, CancellationToken.None);

        Assert.Equal(OldestOpenIssueAgeBand.Fresh, result.Score.Band);
        Assert.Equal(analysisAt, result.AnalysisTimestampUtc);
    }

    // 46. The analyzer holds no clock of its own.
    [Fact]
    public void Analyzer_HoldsNoTimeProviderOrClock()
    {
        var fieldTypes = typeof(OldestOpenIssueAnalyzer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(f => f.FieldType);

        Assert.All(fieldTypes, t =>
        {
            Assert.NotEqual(typeof(TimeProvider), t);
            Assert.NotEqual(typeof(DateTimeOffset), t);
            Assert.NotEqual(typeof(DateTime), t);
        });
    }

    // 47. A future-dated issue is clamped to the freshest band, matching
    // RP-021's clock-skew rule — orchestration adds no policy of its own.
    [Fact]
    public async Task FutureDatedIssue_FollowsScorerClockSkewRule()
    {
        var result = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(5))))
            .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.Succeeded, result.Status);
        Assert.Equal(OldestOpenIssueAgeBand.Fresh, result.Score.Band);
        Assert.Equal(100, result.Score.Value);
    }

    // 48. Determinism: identical inputs produce equal results.
    [Fact]
    public async Task IdenticalInputs_ProduceEqualResults()
    {
        var apiResult = GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-95));

        var first = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
            .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);
        var second = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
            .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.Equal(first, second);
    }

    // 49. Culture cannot change the outcome — no formatting, parsing or
    // culture-sensitive comparison is involved in the decision.
    [Fact]
    public async Task Result_IsIdenticalUnderDifferentCultures()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var apiResult = GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-200));

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turkish = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
                .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var english = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
                .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

            Assert.Equal(turkish, english);
            Assert.Equal(OldestOpenIssueAgeBand.SeverelyStale, turkish.Score.Band);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ---------------------------------------------------------------
    // H. Result invariants
    // ---------------------------------------------------------------

    // 50. Each factory produces exactly its canonical state.
    [Fact]
    public void ResultFactories_ProduceTheirCanonicalStates()
    {
        var succeeded = OldestOpenIssueAnalysisResult.Succeeded(ScoreFor(OldestOpenIssueObservation.NoOpenIssues()), AnalysisAt);
        Assert.Equal(AnalysisSignalStatus.Succeeded, succeeded.Status);
        Assert.Equal(100, succeeded.Score.Value);

        var skipped = OldestOpenIssueAnalysisResult.NotApplicableSkipped(NotApplicableScore(), AnalysisAt);
        Assert.Equal(AnalysisSignalStatus.NotApplicableSkipped, skipped.Status);
        Assert.Null(skipped.Score.Value);
        Assert.Equal(OldestOpenIssueAgeBand.NotApplicable, skipped.Score.Band);

        var failed = OldestOpenIssueAnalysisResult.Failure(AnalysisSignalStatus.Unauthorized, NoDataScore(), AnalysisAt);
        Assert.Equal(AnalysisSignalStatus.Unauthorized, failed.Status);
        Assert.Null(failed.Score.Value);
        Assert.Equal(OldestOpenIssueAgeBand.NoData, failed.Score.Band);
    }

    // 51. Every factory rejects a null score rather than shipping a result
    // whose score cannot be read.
    [Fact]
    public void ResultFactories_RejectNullScore()
    {
        Assert.Throws<ArgumentNullException>(() => OldestOpenIssueAnalysisResult.Succeeded(null!, AnalysisAt));
        Assert.Throws<ArgumentNullException>(() => OldestOpenIssueAnalysisResult.NotApplicableSkipped(null!, AnalysisAt));
        Assert.Throws<ArgumentNullException>(() => OldestOpenIssueAnalysisResult.Failure(AnalysisSignalStatus.Failed, null!, AnalysisAt));
    }

    // 52. A succeeded result cannot carry an unscored band.
    [Fact]
    public void SucceededFactory_RejectsUnscoredBands()
    {
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Succeeded(NoDataScore(), AnalysisAt));
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Succeeded(NotApplicableScore(), AnalysisAt));
    }

    // 53. A skipped result cannot carry anything but NotApplicable.
    [Fact]
    public void NotApplicableFactory_RejectsEveryOtherBand()
    {
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.NotApplicableSkipped(NoDataScore(), AnalysisAt));
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.NotApplicableSkipped(
            ScoreFor(OldestOpenIssueObservation.NoOpenIssues()), AnalysisAt));
    }

    // 54. A failure cannot claim success or a deliberate skip, and cannot
    // carry a scored band.
    [Fact]
    public void FailureFactory_RejectsSuccessOrSkipStatusAndScoredBands()
    {
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Failure(
            AnalysisSignalStatus.Succeeded, NoDataScore(), AnalysisAt));
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Failure(
            AnalysisSignalStatus.NotApplicableSkipped, NoDataScore(), AnalysisAt));
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Failure(
            AnalysisSignalStatus.Unauthorized, ScoreFor(OldestOpenIssueObservation.NoOpenIssues()), AnalysisAt));
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Failure(
            AnalysisSignalStatus.Unauthorized, NotApplicableScore(), AnalysisAt));
    }

    // 55. An undefined status value cannot be smuggled in through the
    // failure factory.
    [Fact]
    public void FailureFactory_RejectsUndefinedStatus()
    {
        Assert.Throws<ArgumentException>(() => OldestOpenIssueAnalysisResult.Failure(
            (AnalysisSignalStatus)999, NoDataScore(), AnalysisAt));
    }

    // 56. Every failure status the analyzer can produce is accepted by the
    // failure factory — the two lists cannot silently drift apart.
    [Fact]
    public void FailureFactory_AcceptsEveryNonSuccessStatus()
    {
        var failureStatuses = Enum.GetValues<AnalysisSignalStatus>()
            .Where(s => s is not (AnalysisSignalStatus.Succeeded or AnalysisSignalStatus.NotApplicableSkipped));

        foreach (var status in failureStatuses)
        {
            var result = OldestOpenIssueAnalysisResult.Failure(status, NoDataScore(), AnalysisAt);
            Assert.Equal(status, result.Status);
        }
    }

    // 57. Construction is closed: no public constructor, no setter, no
    // generic Create, no public static factory.
    [Fact]
    public void Result_HasNoPublicConstructorSetterOrFactory()
    {
        var type = typeof(OldestOpenIssueAnalysisResult);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Assert.Null(p.SetMethod));
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.ReturnType == type);
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), m => m.Name == "Create");
    }

    // 58. The result carries nothing token/session/transport/identity shaped.
    [Fact]
    public void Result_CarriesNoForbiddenMember()
    {
        AssertNoForbiddenMemberNames(
            typeof(OldestOpenIssueAnalysisResult),
            "Token", "Session", "Secret", "Credential", "Password", "Http", "Url", "Header", "Body", "Message",
            "Owner", "Repository", "Exception", "Client", "Context", "FailureKind");
    }

    // 59. The result exposes no type from the API/transport layer and no
    // analysis context — a caller cannot reach back to the request from it.
    [Fact]
    public void Result_ExposesNoApiOrContextType()
    {
        var propertyTypes = typeof(OldestOpenIssueAnalysisResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .ToList();

        Assert.All(propertyTypes, t => Assert.NotEqual("RepoPulse.Core.Repositories", t.Namespace));
        Assert.DoesNotContain(typeof(RepositoryAnalysisContext), propertyTypes);
        Assert.DoesNotContain(typeof(GitHubOldestOpenIssueResult), propertyTypes);
        Assert.DoesNotContain(typeof(CancellationToken), propertyTypes);
        Assert.DoesNotContain(typeof(IGitHubApiClient), propertyTypes);
    }

    // 60. The result exposes exactly the three members this slice defines.
    [Fact]
    public void Result_ExposesExactlyThreeMembers()
    {
        var names = typeof(OldestOpenIssueAnalysisResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .Order()
            .ToArray();

        Assert.Equal(new[] { "AnalysisTimestampUtc", "Score", "Status" }, names);
    }

    // ---------------------------------------------------------------
    // I. Layering: the real mapper and scorer are used
    // ---------------------------------------------------------------

    // 61. For every API outcome, the analyzer's score is exactly what
    // RP-022's mapper feeding RP-021's scorer produces — proving the real
    // components are used rather than reimplemented here.
    [Fact]
    public async Task Score_AlwaysMatchesTheRealMapperAndScorerChain()
    {
        var apiResults = new List<GitHubOldestOpenIssueResult>
        {
            GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-1)),
            GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-45)),
            GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-150)),
            GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-500)),
            GitHubOldestOpenIssueResult.NoOpenIssues()
        };

        apiResults.AddRange(Enum.GetValues<GitHubOldestOpenIssueFailureKind>()
            .Select(GitHubOldestOpenIssueResult.Failure));

        foreach (var apiResult in apiResults)
        {
            var expected = OldestOpenIssueAgeScorer.Score(
                OldestOpenIssueObservationMapper.Map(apiResult),
                AnalysisAt,
                isArchived: false,
                isFork: false);

            var actual = await new OldestOpenIssueAnalyzer(FakeGitHubApiClient.Returning(apiResult))
                .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

            Assert.Equal(expected, actual.Score);
        }
    }

    // 62. The score always carries the scorer's own algorithm version — the
    // orchestration layer never stamps a version of its own.
    [Fact]
    public async Task Score_CarriesTheScorerAlgorithmVersion()
    {
        var succeeded = await new OldestOpenIssueAnalyzer(
                FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues()))
            .AnalyzeAsync(Token, NormalRepository(), AnalysisAt, CancellationToken.None);

        var skipped = await new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient())
            .AnalyzeAsync(Token, RepositoryAnalysisContext.Create(Owner, Name, isArchived: true, isFork: false), AnalysisAt, CancellationToken.None);

        Assert.Equal(OldestOpenIssueAgeScorer.AlgorithmVersion, succeeded.Score.AlgorithmVersion);
        Assert.Equal(OldestOpenIssueAgeScorer.AlgorithmVersion, skipped.Score.AlgorithmVersion);
    }

    // 63. The analyzer's only field is the injected client, and it is
    // readonly — no token, no session, no cached result, no clock.
    [Fact]
    public void Analyzer_HoldsOnlyTheInjectedClient()
    {
        var instanceFields = typeof(OldestOpenIssueAnalyzer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .ToList();

        var field = Assert.Single(instanceFields);
        Assert.Equal(typeof(IGitHubApiClient), field.FieldType);
        Assert.True(field.IsInitOnly);
    }

    // 64. No static state at all — nothing can leak between runs or between
    // callers.
    [Fact]
    public void Analyzer_HasNoStaticState()
    {
        var staticFields = typeof(OldestOpenIssueAnalyzer)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Empty(staticFields);
    }

    // 65. The analyzer exposes no property at all: there is no observable
    // state a late-arriving result could corrupt.
    [Fact]
    public void Analyzer_ExposesNoState()
    {
        Assert.Empty(typeof(OldestOpenIssueAnalyzer).GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    // 66. The public surface never exposes the API result or failure enum —
    // translation happens inside, and only the analysis vocabulary escapes.
    [Fact]
    public void Analyzer_PublicSurfaceExposesNoApiType()
    {
        var method = typeof(OldestOpenIssueAnalyzer).GetMethod(nameof(OldestOpenIssueAnalyzer.AnalyzeAsync))!;
        var types = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType);

        Assert.All(types, t =>
        {
            Assert.NotEqual(typeof(GitHubOldestOpenIssueResult), t);
            Assert.NotEqual(typeof(GitHubOldestOpenIssueFailureKind), t);
            Assert.NotEqual(typeof(GitHubOldestOpenIssueFailureKind?), t);
        });
    }

    // ---------------------------------------------------------------
    // J. Concurrency
    // ---------------------------------------------------------------

    // 67. Two overlapping analyses on the SAME analyzer instance keep their
    // own repository, timestamp and result — a stateless analyzer cannot mix
    // them up.
    [Fact]
    public async Task ConcurrentAnalyses_OnOneInstance_DoNotInterfere()
    {
        var gate = new TaskCompletionSource();
        var client = FakeGitHubApiClient.Handling(async (_, _, repository, _) =>
        {
            await gate.Task;
            return repository == "fresh"
                ? GitHubOldestOpenIssueResult.Success(AnalysisAt.AddDays(-2))
                : GitHubOldestOpenIssueResult.Failure(GitHubOldestOpenIssueFailureKind.Unauthorized);
        });
        var analyzer = new OldestOpenIssueAnalyzer(client);

        var freshTask = analyzer.AnalyzeAsync(
            "token-a", RepositoryAnalysisContext.Create(Owner, "fresh", false, false), AnalysisAt, CancellationToken.None);
        var failingTask = analyzer.AnalyzeAsync(
            "token-b", RepositoryAnalysisContext.Create(Owner, "failing", false, false), AnalysisAt.AddDays(1), CancellationToken.None);

        gate.SetResult();
        var results = await Task.WhenAll(freshTask, failingTask);

        Assert.Equal(AnalysisSignalStatus.Succeeded, results[0].Status);
        Assert.Equal(OldestOpenIssueAgeBand.Fresh, results[0].Score.Band);
        Assert.Equal(AnalysisAt, results[0].AnalysisTimestampUtc);

        Assert.Equal(AnalysisSignalStatus.Unauthorized, results[1].Status);
        Assert.Equal(OldestOpenIssueAgeBand.NoData, results[1].Score.Band);
        Assert.Equal(AnalysisAt.AddDays(1), results[1].AnalysisTimestampUtc);

        Assert.Equal(2, client.OldestOpenIssueCallCount);
        Assert.Contains(client.Calls, c => c.AccessToken == "token-a" && c.Repository == "fresh");
        Assert.Contains(client.Calls, c => c.AccessToken == "token-b" && c.Repository == "failing");
    }

    // 68. Many parallel runs on one instance all produce their own correct
    // answer.
    [Fact]
    public async Task ManyParallelAnalyses_EachProduceTheirOwnCorrectResult()
    {
        var client = FakeGitHubApiClient.Handling((_, _, repository, _) =>
            Task.FromResult(GitHubOldestOpenIssueResult.Success(
                AnalysisAt.AddDays(-int.Parse(repository, CultureInfo.InvariantCulture)))));
        var analyzer = new OldestOpenIssueAnalyzer(client);

        var ages = new[] { 1, 45, 150, 500, 2, 60, 120, 400 };
        var expectedBands = new[]
        {
            OldestOpenIssueAgeBand.Fresh, OldestOpenIssueAgeBand.Aging, OldestOpenIssueAgeBand.Stale, OldestOpenIssueAgeBand.SeverelyStale,
            OldestOpenIssueAgeBand.Fresh, OldestOpenIssueAgeBand.Aging, OldestOpenIssueAgeBand.Stale, OldestOpenIssueAgeBand.SeverelyStale
        };

        var results = await Task.WhenAll(ages.Select(age => analyzer.AnalyzeAsync(
            Token,
            RepositoryAnalysisContext.Create(Owner, age.ToString(CultureInfo.InvariantCulture), false, false),
            AnalysisAt,
            CancellationToken.None)));

        for (var i = 0; i < ages.Length; i++)
        {
            Assert.Equal(AnalysisSignalStatus.Succeeded, results[i].Status);
            Assert.Equal(expectedBands[i], results[i].Score.Band);
        }

        Assert.Equal(ages.Length, client.OldestOpenIssueCallCount);
    }

    // ---------------------------------------------------------------
    // K. Access token contract: null vs. empty/whitespace
    // ---------------------------------------------------------------

    // 69-74. A normal repository with an empty or whitespace token, driven
    // through the REAL GitHubApiClient rather than a fake, so that one test
    // proves the whole delegation chain at once: the analyzer does not
    // duplicate token validation, GitHubApiClient rejects the token before
    // opening a connection, the mapper turns that typed failure into
    // NoData, the scorer scores NoData/null, and the analyzer reports
    // Failed. The FakeHttpMessageHandler's request count proves no HTTP
    // request left the process — it is wired to throw if it is ever asked
    // for a response.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n ")]
    public async Task EmptyOrWhitespaceToken_NormalRepository_FailsWithoutAnyHttpRequest(string accessToken)
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP request may be made for an empty or whitespace token."));
        var analyzer = new OldestOpenIssueAnalyzer(new GitHubApiClient(new HttpClient(handler)));

        var result = await analyzer.AnalyzeAsync(accessToken, NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.Equal(AnalysisSignalStatus.Failed, result.Status);
        Assert.Equal(OldestOpenIssueAgeBand.NoData, result.Score.Band);
        Assert.Null(result.Score.Value);
        Assert.Equal(AnalysisAt, result.AnalysisTimestampUtc);
        Assert.Equal(0, handler.RequestCount);
    }

    // 75. The delegation is real rather than incidental: the same empty
    // token produces exactly the failure GitHubApiClient itself produces
    // for this endpoint, mapped and scored by the real chain.
    [Fact]
    public async Task EmptyToken_ProducesTheSameOutcomeAsTheClientContract()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP request may be made for an empty token."));
        var client = new GitHubApiClient(new HttpClient(handler));

        var apiResult = await client.GetOldestOpenIssueAsync("", Owner, Name, CancellationToken.None);
        var expectedScore = OldestOpenIssueAgeScorer.Score(
            OldestOpenIssueObservationMapper.Map(apiResult), AnalysisAt, isArchived: false, isFork: false);

        var result = await new OldestOpenIssueAnalyzer(client)
            .AnalyzeAsync("", NormalRepository(), AnalysisAt, CancellationToken.None);

        Assert.False(apiResult.IsSuccess);
        Assert.Equal(expectedScore, result.Score);
        Assert.Equal(AnalysisSignalStatus.Failed, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    // 76-80. An archived or fork repository skips the request entirely, so
    // an empty or whitespace token does NOT turn the run into a failure —
    // it still reports NotApplicableSkipped. This is safe rather than a
    // loophole: applicability is decided from repository metadata the
    // caller already holds, so the result asserts nothing about GitHub-side
    // data and needs no authenticated answer.
    [Theory]
    [InlineData(true, false, "")]
    [InlineData(true, false, "   ")]
    [InlineData(false, true, "")]
    [InlineData(false, true, "\t")]
    [InlineData(true, true, " \r\n ")]
    public async Task EmptyOrWhitespaceToken_ArchivedOrFork_StillSkipsWithoutCallingClient(bool isArchived, bool isFork, string accessToken)
    {
        var counting = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());

        var counted = await new OldestOpenIssueAnalyzer(counting).AnalyzeAsync(
            accessToken,
            RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
            AnalysisAt,
            CancellationToken.None);

        // Repeated against a client that throws on every member, so the
        // zero above cannot be a counting mistake.
        var exploding = await new OldestOpenIssueAnalyzer(new ExplodingGitHubApiClient()).AnalyzeAsync(
            accessToken,
            RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
            AnalysisAt,
            CancellationToken.None);

        foreach (var result in new[] { counted, exploding })
        {
            Assert.Equal(AnalysisSignalStatus.NotApplicableSkipped, result.Status);
            Assert.Equal(OldestOpenIssueAgeBand.NotApplicable, result.Score.Band);
            Assert.Null(result.Score.Value);
            Assert.Equal(AnalysisAt, result.AnalysisTimestampUtc);
        }

        Assert.Equal(0, counting.OldestOpenIssueCallCount);
    }

    // 81-83. A null token is a caller error on every repository shape, and
    // it is rejected before the client is ever consulted.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NullToken_ThrowsOnEveryRepositoryShape_WithoutCallingClient(bool isArchived, bool isFork)
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => new OldestOpenIssueAnalyzer(client).AnalyzeAsync(
                null!,
                RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
                AnalysisAt,
                CancellationToken.None));

        Assert.Equal("accessToken", exception.ParamName);
        Assert.Equal(0, client.OldestOpenIssueCallCount);
    }

    // 84. Cancellation outranks the empty-token path too: a pre-cancelled
    // run on a normal repository throws instead of reporting Failed, and
    // no HTTP request is attempted.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PreCancelledToken_NormalRepositoryWithEmptyToken_ThrowsWithoutHttpRequest(string accessToken)
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP request may be made on a cancelled run."));
        var analyzer = new OldestOpenIssueAnalyzer(new GitHubApiClient(new HttpClient(handler)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync(accessToken, NormalRepository(), AnalysisAt, cts.Token));

        Assert.Equal(0, handler.RequestCount);
    }

    // 85-86. And on the archived/fork path, where an empty token would
    // otherwise have produced NotApplicableSkipped: cancellation still
    // wins, with no client call.
    [Theory]
    [InlineData(true, false, "")]
    [InlineData(false, true, "   ")]
    public async Task PreCancelledToken_ArchivedOrForkWithEmptyToken_ThrowsWithoutCallingClient(bool isArchived, bool isFork, string accessToken)
    {
        var client = FakeGitHubApiClient.Returning(GitHubOldestOpenIssueResult.NoOpenIssues());
        var analyzer = new OldestOpenIssueAnalyzer(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync(
                accessToken,
                RepositoryAnalysisContext.Create(Owner, Name, isArchived, isFork),
                AnalysisAt,
                cts.Token));

        Assert.Equal(0, client.OldestOpenIssueCallCount);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static RepositoryAnalysisContext NormalRepository() =>
        RepositoryAnalysisContext.Create(Owner, Name, isArchived: false, isFork: false);

    private static OldestOpenIssueAgeScore ScoreFor(OldestOpenIssueObservation observation) =>
        OldestOpenIssueAgeScorer.Score(observation, AnalysisAt, isArchived: false, isFork: false);

    private static OldestOpenIssueAgeScore NoDataScore() =>
        ScoreFor(OldestOpenIssueObservation.NoData());

    private static OldestOpenIssueAgeScore NotApplicableScore() =>
        OldestOpenIssueAgeScorer.Score(OldestOpenIssueObservation.NoData(), AnalysisAt, isArchived: true, isFork: false);

    private static void AssertNoForbiddenMemberNames(Type type, params string[] forbiddenSubstrings)
    {
        var members = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(type
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

    private readonly record struct RecordedCall(string AccessToken, string Owner, string Repository, CancellationToken CancellationToken);

    // A fake IGitHubApiClient: no HttpClient, no network, and an exact call
    // count so "the request was never made" can be proven rather than
    // assumed. Counting is interlocked because the concurrency tests run
    // overlapping analyses through one instance.
    private sealed class FakeGitHubApiClient : IGitHubApiClient
    {
        private readonly Func<string, string, string, CancellationToken, Task<GitHubOldestOpenIssueResult>> handler;
        private readonly List<RecordedCall> calls = [];
        private readonly Lock callsLock = new();
        private int callCount;

        private FakeGitHubApiClient(Func<string, string, string, CancellationToken, Task<GitHubOldestOpenIssueResult>> handler) =>
            this.handler = handler;

        public static FakeGitHubApiClient Returning(GitHubOldestOpenIssueResult result) =>
            new((_, _, _, _) => Task.FromResult(result));

        public static FakeGitHubApiClient Throwing(Func<Exception> exceptionFactory) =>
            new((_, _, _, _) => throw exceptionFactory());

        public static FakeGitHubApiClient Handling(Func<string, string, string, CancellationToken, Task<GitHubOldestOpenIssueResult>> handler) =>
            new(handler);

        public int OldestOpenIssueCallCount => Volatile.Read(ref callCount);

        public IReadOnlyList<RecordedCall> Calls
        {
            get
            {
                lock (callsLock)
                {
                    return [.. calls];
                }
            }
        }

        public Task<GitHubOldestOpenIssueResult> GetOldestOpenIssueAsync(string accessToken, string owner, string repository, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);

            lock (callsLock)
            {
                calls.Add(new RecordedCall(accessToken, owner, repository, cancellationToken));
            }

            return handler(accessToken, owner, repository, cancellationToken);
        }

        public Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException("RP-023 must not call this endpoint.");

        public Task<GitHubRepositoryResult> GetRepositoryAsync(string accessToken, string owner, string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException("RP-023 must not call this endpoint.");

        public Task<GitHubRepositoryListResult> GetUserRepositoriesAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException("RP-023 must not call this endpoint.");

        public Task<GitHubLatestCommitResult> GetLatestRepositoryCommitAsync(string accessToken, string owner, string repository, CancellationToken cancellationToken) =>
            throw new NotSupportedException("RP-023 must not call this endpoint.");

        public Task<GitHubCommitCountResult> GetDefaultBranchCommitCountAsync(string accessToken, string owner, string repository, DateTimeOffset sinceUtc, DateTimeOffset untilUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException("RP-023 must not call this endpoint.");
    }

    // Throws on EVERY member. Used where the point of the test is that the
    // analyzer must not touch the client at all.
    private sealed class ExplodingGitHubApiClient : IGitHubApiClient
    {
        public Task<GitHubOldestOpenIssueResult> GetOldestOpenIssueAsync(string accessToken, string owner, string repository, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GitHub request may be made on this path.");

        public Task<GitHubUserResult> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GitHub request may be made on this path.");

        public Task<GitHubRepositoryResult> GetRepositoryAsync(string accessToken, string owner, string name, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GitHub request may be made on this path.");

        public Task<GitHubRepositoryListResult> GetUserRepositoriesAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GitHub request may be made on this path.");

        public Task<GitHubLatestCommitResult> GetLatestRepositoryCommitAsync(string accessToken, string owner, string repository, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GitHub request may be made on this path.");

        public Task<GitHubCommitCountResult> GetDefaultBranchCommitCountAsync(string accessToken, string owner, string repository, DateTimeOffset sinceUtc, DateTimeOffset untilUtc, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GitHub request may be made on this path.");
    }
}
