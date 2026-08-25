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
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

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
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

        var result = await store.SignInAsync(CreateSession(), CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Null(userStore.Current);
    }

    [Fact]
    public async Task SignInAsync_InvalidSession_NeverAttemptsStorageWrite_MemoryCleared()
    {
        var storage = new FakeSecureSessionStorage();
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

        var result = await store.SignInAsync(CreateSession(accessToken: ""), CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Equal(0, storage.SetCallCount);
    }

    [Fact]
    public async Task SignInAsync_Success_ClearsAnyPriorInvalidationMarker()
    {
        var storage = new FakeSecureSessionStorage();
        var marker = new FakeSessionInvalidationMarker { IsInvalidated = true };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

        var result = await store.SignInAsync(CreateSession(), CancellationToken.None);

        Assert.True(result);
        Assert.False(marker.IsInvalidated);
        Assert.Equal(1, marker.ClearCallCount);
    }

    [Fact]
    public async Task SignInAsync_MarkerClearThrows_SignInStillSucceeds()
    {
        var storage = new FakeSecureSessionStorage();
        var marker = new FakeSessionInvalidationMarker
        {
            IsInvalidated = true,
            ClearException = new InvalidOperationException("MARKER-CLEAR-FAILURE")
        };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

        var result = await store.SignInAsync(CreateSession(), CancellationToken.None);

        // The marker write failing doesn't crash or block sign-in this
        // session — worst case, restore is unnecessarily rejected once
        // more on a future cold start until a later sign-in's clear
        // succeeds. Never a security regression, just an extra re-login.
        Assert.True(result);
        Assert.True(userStore.IsSignedIn);
    }

    [Fact]
    public async Task RestoreAsync_ValidStoredSession_PopulatesMemoryAndReturnsTrue()
    {
        var storage = new FakeSecureSessionStorage();
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);
        await store.SignInAsync(CreateSession(), CancellationToken.None);

        var freshUserStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, marker, freshUserStore);

        var result = await restoreStore.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result);
        Assert.True(freshUserStore.IsSignedIn);
        Assert.Equal("octocat", freshUserStore.Current!.Login);
    }

    [Fact]
    public async Task RestoreAsync_NoStoredSession_ReturnsFalse_MemoryUntouched()
    {
        var storage = new FakeSecureSessionStorage { StoredValue = null };
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

        var result = await store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
    }

    [Fact]
    public async Task RestoreAsync_CorruptStoredPayload_RemovesKeyAndReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage { StoredValue = "{ this is not valid json" };
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

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
        var marker = new FakeSessionInvalidationMarker();
        var seedStore = new SessionPersistenceStore(storage, marker, new UserSessionStore());
        var expiredSession = new UserSession("token", null, "octocat", null, DateTimeOffset.UtcNow.AddMinutes(-30));
        await seedStore.SignInAsync(expiredSession, CancellationToken.None);

        var userStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, marker, userStore);

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
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

        var result = await store.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Equal(1, storage.RemoveCallCount);
    }

    [Fact]
    public async Task SignOutAsync_RemoveSucceeds_ClearsPersistedAndMemory_ReturnsTrue()
    {
        var storage = new FakeSecureSessionStorage();
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);
        await store.SignInAsync(CreateSession(), CancellationToken.None);

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.True(result);
        Assert.False(userStore.IsSignedIn);
        Assert.Null(storage.StoredValue);
        // A clean removal never needs the fallback marker.
        Assert.False(marker.IsInvalidated);
    }

    [Fact]
    public async Task SignOutAsync_RemoveThrows_MemoryIsNotCleared_ReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage
        {
            RemoveException = new InvalidOperationException("MARKER-REMOVE-FAILURE")
        };
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);
        userStore.SignIn(CreateSession());

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.False(result);
        // The whole point of this ordering: never let the caller believe
        // sign-out succeeded (and navigate to Login) while the persisted
        // copy — which would silently restore the "signed out" user again
        // on next launch — might still be sitting on disk.
        Assert.True(userStore.IsSignedIn);
        // The removal failure must set the fallback invalidation marker —
        // this is what stops the still-present stale payload from being
        // trusted by a later RestoreAsync.
        Assert.True(marker.IsInvalidated);
    }

    [Fact]
    public async Task SignOutAsync_NoPersistedValueToRemove_StillSucceeds()
    {
        var storage = new FakeSecureSessionStorage { StoredValue = null };
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);
        userStore.SignIn(CreateSession());

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.True(result);
        Assert.False(userStore.IsSignedIn);
    }

    [Fact]
    public async Task SignOutAsync_RemoveThrows_MarkerSetAlsoThrows_DoesNotCrash_StillReturnsFalse()
    {
        var storage = new FakeSecureSessionStorage
        {
            RemoveException = new InvalidOperationException("MARKER-REMOVE-FAILURE")
        };
        var marker = new FakeSessionInvalidationMarker
        {
            SetException = new InvalidOperationException("MARKER-SET-FAILURE")
        };
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);
        userStore.SignIn(CreateSession());

        var result = await store.SignOutAsync(CancellationToken.None);

        Assert.False(result);
        Assert.True(userStore.IsSignedIn);
    }

    // This is the regression test for the confirmed stale-session-restore
    // bug: a 401 handler calls SignOutAsync, the persisted key's removal
    // throws (so the stale, now-invalid-per-GitHub payload is still on
    // disk), and the user is routed to Login regardless. Without the
    // invalidation marker, a later RestoreAsync would happily parse and
    // restore that exact stale payload again.
    [Fact]
    public async Task RestoreAsync_AfterFailedSignOutRemoval_NeverRestoresStaleSession()
    {
        var storage = new FakeSecureSessionStorage();
        var marker = new FakeSessionInvalidationMarker();

        var seedUserStore = new UserSessionStore();
        var seedStore = new SessionPersistenceStore(storage, marker, seedUserStore);
        await seedStore.SignInAsync(CreateSession(), CancellationToken.None);

        // Now simulate the persisted key's removal failing (e.g. the 401
        // handler's SignOutAsync call), leaving the stale, already-invalid
        // payload physically present in storage.
        storage.RemoveException = new InvalidOperationException("MARKER-REMOVE-FAILURE");
        var signOutResult = await seedStore.SignOutAsync(CancellationToken.None);
        Assert.False(signOutResult);
        Assert.NotNull(storage.StoredValue);

        var freshUserStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, marker, freshUserStore);

        var restored = await restoreStore.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(restored);
        Assert.False(freshUserStore.IsSignedIn);
    }

    [Fact]
    public async Task RestoreAsync_MarkerSet_ReturnsFalse_EvenIfStoredPayloadIsOtherwiseValid()
    {
        var storage = new FakeSecureSessionStorage();
        var seedMarker = new FakeSessionInvalidationMarker();
        var seedStore = new SessionPersistenceStore(storage, seedMarker, new UserSessionStore());
        await seedStore.SignInAsync(CreateSession(), CancellationToken.None);
        Assert.NotNull(storage.StoredValue);

        var invalidatedMarker = new FakeSessionInvalidationMarker { IsInvalidated = true };
        var userStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, invalidatedMarker, userStore);

        var result = await restoreStore.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
    }

    [Fact]
    public async Task RestoreAsync_MarkerCheckThrows_FailsClosed_TreatsAsInvalidated()
    {
        var storage = new FakeSecureSessionStorage();
        var seedStore = new SessionPersistenceStore(storage, new FakeSessionInvalidationMarker(), new UserSessionStore());
        await seedStore.SignInAsync(CreateSession(), CancellationToken.None);

        var marker = new FakeSessionInvalidationMarker
        {
            IsSetException = new InvalidOperationException("MARKER-READ-FAILURE")
        };
        var userStore = new UserSessionStore();
        var restoreStore = new SessionPersistenceStore(storage, marker, userStore);

        var result = await restoreStore.RestoreAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result);
        Assert.False(userStore.IsSignedIn);
    }

    [Fact]
    public async Task ConcurrentSignInRestoreSignOut_AreSerialized_NeverOverlap()
    {
        var storage = new FakeSecureSessionStorage { OperationDelay = TimeSpan.FromMilliseconds(30) };
        var marker = new FakeSessionInvalidationMarker();
        var userStore = new UserSessionStore();
        var store = new SessionPersistenceStore(storage, marker, userStore);

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

    private sealed class FakeSessionInvalidationMarker : ISessionInvalidationMarker
    {
        public bool IsInvalidated { get; set; }
        public Exception? IsSetException { get; set; }
        public Exception? SetException { get; set; }
        public Exception? ClearException { get; set; }

        public int SetCallCount { get; private set; }
        public int ClearCallCount { get; private set; }

        public Task<bool> IsSetAsync()
        {
            if (IsSetException is not null)
            {
                throw IsSetException;
            }

            return Task.FromResult(IsInvalidated);
        }

        public Task SetAsync()
        {
            SetCallCount++;
            if (SetException is not null)
            {
                throw SetException;
            }

            IsInvalidated = true;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            ClearCallCount++;
            if (ClearException is not null)
            {
                throw ClearException;
            }

            IsInvalidated = false;
            return Task.CompletedTask;
        }
    }
}
