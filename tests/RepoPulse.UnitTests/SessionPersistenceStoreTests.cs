using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class SessionPersistenceStoreTests
{
    private static UserSession CreateSession(string accessToken = "FAKE-ACCESS-TOKEN-TEST-ONLY") =>
        new(accessToken, "FAKE-REFRESH-TOKEN-TEST-ONLY", "octocat", "https://example.com/avatar.png");

    [Fact]
    public async Task SignInAsync_StorageSucceeds_PersistsAndPopulatesMemory()
    {
        var storage = new FakeSecureSessionStorage();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var result = await store.SignInAsync(CreateSession(), CancellationToken.None);

        Assert.True(result);
        Assert.True(userStore.IsSignedIn);
        Assert.NotNull(storage.StoredValue);
        Assert.Equal(1, storage.SetCallCount);
    }

    [Fact]
    public async Task SignInAsync_StorageSetThrows_MemoryIsClearedAndFalseReturned()
    {
        var storage = new FakeSecureSessionStorage
        {
            SetException = new InvalidOperationException("MARKER-SENSITIVE-EXCEPTION-TEXT-9f3a")
        };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var result = await store.SignInAsync(CreateSession(), CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Null(userStore.Current);
    }

    [Fact]
    public async Task SignInAsync_InvalidSession_NeverAttemptsStorageWrite_MemoryCleared()
    {
        var storage = new FakeSecureSessionStorage();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var result = await store.SignInAsync(CreateSession(accessToken: ""), CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Equal(0, storage.SetCallCount);
    }

    [Fact]
    public async Task RestoreAsync_ValidStoredSession_PopulatesMemoryAndReturnsTrue()
    {
        var storage = new FakeSecureSessionStorage();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);
        await store.SignInAsync(CreateSession(), CancellationToken.None);

        var freshUserStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, freshUserStore);

        var result = await restoreStore.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result);
        Assert.True(freshUserStore.IsSignedIn);
        Assert.Equal("octocat", freshUserStore.Current!.Login);
    }

    [Fact]
    public async Task RestoreAsync_NoStoredSession_ReturnsFalse_MemoryUntouched()
    {
        var storage = new FakeSecureSessionStorage { StoredValue = null };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var result = await store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
    }

    [Fact]
    public async Task RestoreAsync_CorruptStoredPayload_RemovesKeyAndReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage { StoredValue = "{ this is not valid json" };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var result = await store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Equal(1, storage.RemoveCallCount);
        Assert.Null(storage.StoredValue);
    }

    [Fact]
    public async Task RestoreAsync_ExpiredStoredPayload_RemovesKeyAndReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage();
        var seedStore = new SessionPersistenceStore(storage, new UserSessionStore());
        var expiredSession = new UserSession("token", null, "octocat", null, DateTimeOffset.UtcNow.AddMinutes(-30));
        await seedStore.SignInAsync(expiredSession, CancellationToken.None);

        var userStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, userStore);

        var result = await restoreStore.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Null(storage.StoredValue);
    }

    [Fact]
    public async Task RestoreAsync_StorageGetThrows_DoesNotCrash_AttemptsRemoveAndReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage
        {
            GetException = new InvalidOperationException("MARKER-UNDECRYPTABLE-ANDROID-KEYSTORE-VALUE")
        };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var result = await store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Equal(1, storage.RemoveCallCount);
    }

    [Fact]
    public async Task SignOutAsync_RemoveSucceeds_ClearsPersistedAndMemory_ReturnsTrue()
    {
        var storage = new FakeSecureSessionStorage();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);
        await store.SignInAsync(CreateSession(), CancellationToken.None);

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.True(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Null(storage.StoredValue);
    }

    [Fact]
    public async Task SignOutAsync_RemoveThrows_MemoryIsNotCleared_ReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage
        {
            RemoveException = new InvalidOperationException("MARKER-REMOVE-FAILURE")
        };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);
        userStore.SignIn(CreateSession());

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.False(result);
        // The whole point of this ordering: never let the caller believe
        // sign-out succeeded (and navigate to Login) while the persisted
        // copy — which would silently restore the "signed out" user again
        // on next launch — might still be sitting on disk.
        Assert.True(userStore.IsSignedIn);
    }

    [Fact]
    public async Task SignOutAsync_NoPersistedValueToRemove_StillSucceeds()
    {
        var storage = new FakeSecureSessionStorage { StoredValue = null };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);
        userStore.SignIn(CreateSession());

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.True(result);
        Assert.False(userStore.IsSignedIn);
    }

    [Fact]
    public async Task ConcurrentSignInRestoreSignOut_AreSerialized_NeverOverlap()
    {
        var storage = new FakeSecureSessionStorage { OperationDelay = TimeSpan.FromMilliseconds(30) };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, userStore);

        var tasks = new[]
        {
            store.SignInAsync(CreateSession(), CancellationToken.None),
            store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None),
            store.SignOutAsync(CancellationToken.None),
            store.SignInAsync(CreateSession(), CancellationToken.None),
            store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None),
        };

        await Task.WhenAll(tasks);

        Assert.True(storage.MaxObservedConcurrentCalls <= 1);
    }

    private sealed class FakeSecureSessionStorage : ISecureSessionStorage
    {
        private readonly object concurrencyLock = new();
        private int activeCalls;

        public string? StoredValue { get; set; }
        public Exception? GetException { get; set; }
        public Exception? SetException { get; set; }
        public Exception? RemoveException { get; set; }
        public TimeSpan OperationDelay { get; set; } = TimeSpan.Zero;

        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }
        public int MaxObservedConcurrentCalls { get; private set; }

        public async Task<string?> GetAsync()
        {
            GetCallCount++;
            await TrackConcurrencyAsync();
            if (GetException is not null)
            {
                throw GetException;
            }

            return StoredValue;
        }

        public async Task SetAsync(string value)
        {
            SetCallCount++;
            await TrackConcurrencyAsync();
            if (SetException is not null)
            {
                throw SetException;
            }

            StoredValue = value;
        }

        public async Task RemoveAsync()
        {
            RemoveCallCount++;
            await TrackConcurrencyAsync();
            if (RemoveException is not null)
            {
                throw RemoveException;
            }

            StoredValue = null;
        }

        private async Task TrackConcurrencyAsync()
        {
            lock (concurrencyLock)
            {
                activeCalls++;
                if (activeCalls > MaxObservedConcurrentCalls)
                {
                    MaxObservedConcurrentCalls = activeCalls;
                }
            }

            if (OperationDelay > TimeSpan.Zero)
            {
                await Task.Delay(OperationDelay);
            }
            else
            {
                await Task.Yield();
            }

            lock (concurrencyLock)
            {
                activeCalls--;
            }
        }
    }
}
