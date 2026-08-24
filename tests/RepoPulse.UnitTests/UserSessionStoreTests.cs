using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class UserSessionStoreTests
{
    [Fact]
    public void NewStore_IsNotSignedIn()
    {
        var store = new UserSessionStore();

        Assert.False(store.IsSignedIn);
        Assert.Null(store.Current);
    }

    [Fact]
    public void SignIn_SetsCurrentSessionAndIsSignedIn()
    {
        var store = new UserSessionStore();
        var session = new UserSession("access-token", "refresh-token", "octocat", "https://example.com/a.png");

        store.SignIn(session);

        Assert.True(store.IsSignedIn);
        Assert.Same(session, store.Current);
    }

    [Fact]
    public void SignIn_WithoutRefreshTokenOrAvatar_StillSucceeds()
    {
        var store = new UserSessionStore();
        var session = new UserSession("access-token", null, "octocat", null);

        store.SignIn(session);

        Assert.True(store.IsSignedIn);
        Assert.Null(store.Current!.RefreshToken);
        Assert.Null(store.Current.AvatarUrl);
    }

    [Fact]
    public void SignOut_ClearsSession_NothingRemains()
    {
        var store = new UserSessionStore();
        store.SignIn(new UserSession("access-token", "refresh-token", "octocat", "https://example.com/a.png"));

        store.SignOut();

        Assert.False(store.IsSignedIn);
        Assert.Null(store.Current);
    }

    [Fact]
    public void SignOut_WhenAlreadySignedOut_IsSafeNoOp()
    {
        var store = new UserSessionStore();

        store.SignOut();

        Assert.False(store.IsSignedIn);
        Assert.Null(store.Current);
    }

    [Fact]
    public void SignIn_ReplacesAnyPreviousSession()
    {
        var store = new UserSessionStore();
        store.SignIn(new UserSession("first-token", null, "first-user", null));

        store.SignIn(new UserSession("second-token", null, "second-user", null));

        Assert.Equal("second-user", store.Current!.Login);
        Assert.Equal("second-token", store.Current.AccessToken);
    }

    [Fact]
    public void SignIn_WithoutAccessTokenExpiration_DefaultsToNull()
    {
        var store = new UserSessionStore();

        store.SignIn(new UserSession("access-token", null, "octocat", null));

        Assert.Null(store.Current!.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public void SignIn_WithAccessTokenExpiration_StoresIt()
    {
        var store = new UserSessionStore();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        store.SignIn(new UserSession("access-token", null, "octocat", null, expiresAt));

        Assert.Equal(expiresAt, store.Current!.AccessTokenExpiresAtUtc);
    }
}
