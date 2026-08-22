using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class AuthorizationSessionStoreTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public void TryStart_FirstCall_Succeeds()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));

        var started = store.TryStart(Lifetime, out var session);

        Assert.True(started);
        Assert.False(string.IsNullOrEmpty(session.State));
        Assert.False(string.IsNullOrEmpty(session.CodeVerifier));
        Assert.False(string.IsNullOrEmpty(session.CodeChallenge));
    }

    [Fact]
    public void TryConsume_CorrectState_Succeeds()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out var session);

        var consumed = store.TryConsume(session.State, out var consumedSession);

        Assert.True(consumed);
        Assert.Equal(session.CodeVerifier, consumedSession!.CodeVerifier);
    }

    [Fact]
    public void TryConsume_WrongState_Fails()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out _);

        var consumed = store.TryConsume("not-the-real-state", out var consumedSession);

        Assert.False(consumed);
        Assert.Null(consumedSession);
    }

    [Fact]
    public void TryConsume_MissingState_Fails()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out _);

        Assert.False(store.TryConsume(null, out _));
        Assert.False(store.TryConsume(string.Empty, out _));
    }

    [Fact]
    public void TryConsume_NoSessionEverStarted_Fails()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));

        Assert.False(store.TryConsume("anything", out _));
    }

    [Fact]
    public void TryConsume_ExpiredSession_Fails()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new AuthorizationSessionStore(time);
        store.TryStart(Lifetime, out var session);

        time.Advance(Lifetime + TimeSpan.FromSeconds(1));

        Assert.False(store.TryConsume(session.State, out _));
    }

    [Fact]
    public void TryConsume_SameSessionTwice_SecondAttemptFails()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out var session);

        var firstConsume = store.TryConsume(session.State, out _);
        var secondConsume = store.TryConsume(session.State, out _);

        Assert.True(firstConsume);
        Assert.False(secondConsume);
    }

    [Fact]
    public void TryStart_WhilePendingSessionActive_SecondAttemptIsRejected()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out var firstSession);

        var secondStarted = store.TryStart(Lifetime, out _);

        Assert.False(secondStarted);
        // The original session must still be the one that can be consumed.
        Assert.True(store.TryConsume(firstSession.State, out _));
    }

    [Fact]
    public void TryStart_AfterPriorSessionWasConsumed_NewAttemptSucceeds()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out var firstSession);
        store.TryConsume(firstSession.State, out _);

        var started = store.TryStart(Lifetime, out var secondSession);

        Assert.True(started);
        Assert.NotEqual(firstSession.State, secondSession.State);
    }

    [Fact]
    public void TryStart_AfterPriorSessionExpired_NewAttemptSucceeds()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new AuthorizationSessionStore(time);
        store.TryStart(Lifetime, out _);

        time.Advance(Lifetime + TimeSpan.FromSeconds(1));

        Assert.True(store.TryStart(Lifetime, out _));
    }

    [Fact]
    public void Reset_ClearsPendingSession()
    {
        var store = new AuthorizationSessionStore(new ManualTimeProvider(DateTimeOffset.UtcNow));
        store.TryStart(Lifetime, out var session);

        store.Reset();

        Assert.False(store.TryConsume(session.State, out _));
    }
}
