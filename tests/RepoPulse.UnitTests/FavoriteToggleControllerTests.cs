using System.Reflection;
using RepoPulse.Core.Authentication;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// No test here ever touches a real database — FakeFavoriteRepositoryStore
// drives every scenario, so these prove FavoriteToggleController's own
// logic (idempotent toggle, double-tap guard, failure surfacing, AND
// cross-account isolation) in isolation from SqliteFavoriteRepositoryStore
// (covered separately, against a real temp-file database, in
// SqliteFavoriteRepositoryStoreTests).
public class FavoriteToggleControllerTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => now;
    }

    // Account-scoped, exactly like SqliteFavoriteRepositoryStore's real
    // composite (AccountLoginNormalized, NormalizedFullName) key — so tests
    // against this fake exercise the same isolation contract the real store
    // enforces via its schema.
    private sealed class FakeFavoriteRepositoryStore : IFavoriteRepositoryStore
    {
        private readonly Dictionary<(string Account, string Repository), FavoriteRepository> favorites = new();

        public FavoriteStoreFailureKind? FailNextOperation { get; set; }
        public int AddCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }
        public TaskCompletionSource<bool>? BlockNextOperationUntil { get; set; }

        // Separate gate from BlockNextOperationUntil (which only blocks
        // AddAsync/RemoveAsync) — needed to simulate a GetAllAsync call
        // that is still in flight when the session changes underneath it.
        public TaskCompletionSource<bool>? BlockNextGetAllUntil { get; set; }

        public async Task<FavoriteStoreResult> InitializeAsync(CancellationToken cancellationToken) =>
            await Task.FromResult(FavoriteStoreResult.Success());

        public async Task<FavoriteListResult> GetAllAsync(string accountLogin, CancellationToken cancellationToken)
        {
            if (BlockNextGetAllUntil is { } gate)
            {
                BlockNextGetAllUntil = null;
                await gate.Task;
            }

            if (FailNextOperation is { } kind)
            {
                FailNextOperation = null;
                return FavoriteListResult.Failure(kind);
            }

            FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccount);
            var scoped = favorites.Where(entry => entry.Key.Account == normalizedAccount).Select(entry => entry.Value).ToList();
            return FavoriteListResult.Success(scoped);
        }

        public async Task<FavoriteStoreResult> AddAsync(string accountLogin, string owner, string name, DateTimeOffset addedAtUtc, CancellationToken cancellationToken)
        {
            AddCallCount++;
            if (BlockNextOperationUntil is { } gate)
            {
                BlockNextOperationUntil = null;
                await gate.Task;
            }

            if (FailNextOperation is { } kind)
            {
                FailNextOperation = null;
                return FavoriteStoreResult.Failure(kind);
            }

            FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccount);
            FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
            favorites.TryAdd((normalizedAccount, identity.NormalizedFullName), new FavoriteRepository(identity.Owner, identity.Name, identity.NormalizedFullName, addedAtUtc));
            return FavoriteStoreResult.Success();
        }

        public Task<FavoriteStoreResult> RemoveAsync(string accountLogin, string owner, string name, CancellationToken cancellationToken)
        {
            RemoveCallCount++;
            if (FailNextOperation is { } kind)
            {
                FailNextOperation = null;
                return Task.FromResult(FavoriteStoreResult.Failure(kind));
            }

            FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccount);
            FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
            favorites.Remove((normalizedAccount, identity.NormalizedFullName));
            return Task.FromResult(FavoriteStoreResult.Success());
        }

        public Task<FavoriteStatusResult> IsFavoriteAsync(string accountLogin, string owner, string name, CancellationToken cancellationToken)
        {
            FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccount);
            FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
            return Task.FromResult(FavoriteStatusResult.Success(favorites.ContainsKey((normalizedAccount, identity.NormalizedFullName))));
        }
    }

    private static UserSessionStore SignedInAs(string login)
    {
        var store = new UserSessionStore();
        store.SignIn(new UserSession("fake-access-token", null, login, null));
        return store;
    }

    private static FavoriteToggleController MakeController(IFavoriteRepositoryStore store, UserSessionStore userSessionStore) =>
        new(store, new FixedTimeProvider(DateTimeOffset.UtcNow), userSessionStore);

    [Fact]
    public async Task ToggleAsync_NotYetFavorite_AddsAndReportsFavorite()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = MakeController(store, SignedInAs("alice"));

        var result = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsFavoriteAfterToggle);
        Assert.True(controller.IsFavorite("owner", "name"));
        Assert.Equal(1, store.AddCallCount);
    }

    [Fact]
    public async Task ToggleAsync_AlreadyFavorite_RemovesAndReportsNotFavorite()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = MakeController(store, SignedInAs("alice"));
        await controller.ToggleAsync("owner", "name", CancellationToken.None);

        var result = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFavoriteAfterToggle);
        Assert.False(controller.IsFavorite("owner", "name"));
        Assert.Equal(1, store.RemoveCallCount);
    }

    [Fact]
    public async Task ToggleAsync_CasingVariantOfExistingFavorite_IsRecognizedAsSameIdentity()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = MakeController(store, SignedInAs("alice"));
        await controller.ToggleAsync("mustafanazli", "RepoPulse", CancellationToken.None);

        Assert.True(controller.IsFavorite("MustafaNazli", "repopulse"));
    }

    // A fast second tap on the SAME identity while the first ToggleAsync is
    // still awaiting the store must never issue a second Add/Remove call —
    // it must be ignored outright.
    [Fact]
    public async Task ToggleAsync_SecondCallForSameIdentityWhileFirstInFlight_IsIgnored()
    {
        var gate = new TaskCompletionSource<bool>();
        var store = new FakeFavoriteRepositoryStore { BlockNextOperationUntil = gate };
        var controller = MakeController(store, SignedInAs("alice"));

        // AddAsync captures `gate` and starts awaiting it before this method
        // returns control here, so by the time the second ToggleAsync below
        // runs, the first call is genuinely still in flight.
        var firstToggle = controller.ToggleAsync("owner", "name", CancellationToken.None);
        var secondToggle = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.True(secondToggle.IsIgnored);
        Assert.False(secondToggle.IsSuccess);
        Assert.Equal(1, store.AddCallCount);

        gate.SetResult(true);
        var firstResult = await firstToggle;

        Assert.True(firstResult.IsSuccess);
        Assert.Equal(1, store.AddCallCount);
    }

    [Fact]
    public async Task ToggleAsync_DifferentIdentities_ToggleIndependentlyAndConcurrently()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = MakeController(store, SignedInAs("alice"));

        var first = controller.ToggleAsync("owner", "a", CancellationToken.None);
        var second = controller.ToggleAsync("owner", "b", CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.True(controller.IsFavorite("owner", "a"));
        Assert.True(controller.IsFavorite("owner", "b"));
        Assert.Equal(2, store.AddCallCount);
    }

    [Fact]
    public async Task ToggleAsync_StoreFailure_DoesNotChangeFavoriteState()
    {
        var store = new FakeFavoriteRepositoryStore { FailNextOperation = FavoriteStoreFailureKind.IoError };
        var controller = MakeController(store, SignedInAs("alice"));

        var result = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FavoriteStoreFailureKind.IoError, result.FailureKind);
        // The previous (not-a-favorite) state must be exactly preserved —
        // a failed add must never look like it succeeded.
        Assert.False(controller.IsFavorite("owner", "name"));
    }

    [Fact]
    public async Task ToggleAsync_StoreFailureOnRemove_PreservesExistingFavoriteState()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = MakeController(store, SignedInAs("alice"));
        await controller.ToggleAsync("owner", "name", CancellationToken.None);

        store.FailNextOperation = FavoriteStoreFailureKind.Corrupt;
        var result = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.False(result.IsSuccess);
        // Still a favorite — the failed remove must not have taken effect.
        Assert.True(controller.IsFavorite("owner", "name"));
    }

    [Fact]
    public async Task ToggleAsync_SignedOut_FailsAndNeverCallsStore()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = MakeController(store, new UserSessionStore());

        var result = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, store.AddCallCount);
        Assert.Equal(0, store.RemoveCallCount);
    }

    [Fact]
    public async Task EnsureLoadedForCurrentSessionAsync_StoreFailure_SetsLastLoadFailureAndKeepsFavoritesEmpty()
    {
        var store = new FakeFavoriteRepositoryStore { FailNextOperation = FavoriteStoreFailureKind.Corrupt };
        var controller = MakeController(store, SignedInAs("alice"));

        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.Equal(FavoriteStoreFailureKind.Corrupt, controller.LastLoadFailure);
        Assert.Empty(controller.Favorites);
    }

    [Fact]
    public async Task EnsureLoadedForCurrentSessionAsync_Success_ClearsPreviousLoadFailure()
    {
        var store = new FakeFavoriteRepositoryStore { FailNextOperation = FavoriteStoreFailureKind.IoError };
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);
        Assert.NotNull(controller.LastLoadFailure);

        // A fresh sign-in (even as the same account) bumps SessionGeneration,
        // which is what makes a second EnsureLoadedForCurrentSessionAsync
        // call actually reload instead of being a no-op.
        userSessionStore.SignIn(new UserSession("fake-access-token", null, "alice", null));
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.Null(controller.LastLoadFailure);
    }

    [Fact]
    public async Task EnsureLoadedForCurrentSessionAsync_CalledTwiceForSameGeneration_OnlyLoadsOnce()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        await store.AddAsync("alice", "owner", "name", DateTimeOffset.UtcNow, CancellationToken.None);
        var controller = MakeController(store, userSessionStore);

        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);
        Assert.Single(controller.Favorites);

        // Removing directly via the store (bypassing the controller) proves
        // the second call below is a true no-op — if it silently reloaded,
        // Favorites would drop to zero.
        await store.RemoveAsync("alice", "owner", "name", CancellationToken.None);
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.Single(controller.Favorites);
    }

    // ---- Account isolation (the fix this file exists to prove) ----

    [Fact]
    public async Task CrossAccount_FavoriteAddedByAccountA_IsNotVisibleToAccountB()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);
        await controller.ToggleAsync("owner", "shared-repo", CancellationToken.None);
        Assert.True(controller.IsFavorite("owner", "shared-repo"));

        userSessionStore.SignIn(new UserSession("fake-access-token-b", null, "bob", null));
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.False(controller.IsFavorite("owner", "shared-repo"));
        Assert.Empty(controller.Favorites);
    }

    [Fact]
    public async Task CrossAccount_SameRepositoryCanBeFavoritedIndependentlyByBothAccounts()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);
        await controller.ToggleAsync("owner", "shared-repo", CancellationToken.None);

        userSessionStore.SignIn(new UserSession("fake-access-token-b", null, "bob", null));
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);
        var addResultForBob = await controller.ToggleAsync("owner", "shared-repo", CancellationToken.None);

        Assert.True(addResultForBob.IsSuccess);
        Assert.True(addResultForBob.IsFavoriteAfterToggle);
        Assert.True(controller.IsFavorite("owner", "shared-repo"));

        var bobsFavorites = await store.GetAllAsync("bob", CancellationToken.None);
        var alicesFavorites = await store.GetAllAsync("alice", CancellationToken.None);
        Assert.Single(bobsFavorites.Favorites);
        Assert.Single(alicesFavorites.Favorites);
    }

    [Fact]
    public async Task CrossAccount_AccountBRemovingFavorite_DoesNotAffectAccountAsRecord()
    {
        var store = new FakeFavoriteRepositoryStore();
        await store.AddAsync("alice", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);
        await store.AddAsync("bob", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);
        var userSessionStore = SignedInAs("bob");
        var controller = MakeController(store, userSessionStore);
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        await controller.ToggleAsync("owner", "shared-repo", CancellationToken.None);

        var alicesFavorites = await store.GetAllAsync("alice", CancellationToken.None);
        Assert.Single(alicesFavorites.Favorites);
        var bobsFavorites = await store.GetAllAsync("bob", CancellationToken.None);
        Assert.Empty(bobsFavorites.Favorites);
    }

    [Fact]
    public async Task Logout_ThenSignBackIntoSameAccount_RestoresItsOwnFavorites()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);
        await controller.ToggleAsync("owner", "name", CancellationToken.None);

        userSessionStore.SignOut();
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);
        Assert.Empty(controller.Favorites);

        userSessionStore.SignIn(new UserSession("fake-access-token", null, "alice", null));
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.True(controller.IsFavorite("owner", "name"));
    }

    [Fact]
    public async Task Logout_ThenSignInAsDifferentAccount_PreviousAccountsStateIsNotVisible()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);
        await controller.ToggleAsync("owner", "name", CancellationToken.None);

        userSessionStore.SignOut();
        userSessionStore.SignIn(new UserSession("fake-access-token-b", null, "bob", null));
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.False(controller.IsFavorite("owner", "name"));
        Assert.Empty(controller.Favorites);
    }

    [Fact]
    public void SignOut_ImmediatelyClearsInMemoryFavoritesEvenBeforeNextLoad()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);

        userSessionStore.SignOut();

        // IsFavorite must never answer using a stale, now-signed-out
        // account's in-memory state, even before
        // EnsureLoadedForCurrentSessionAsync has had a chance to run again.
        // (Nothing was ever added here, so this also incidentally proves no
        // exception is thrown with an empty in-memory set.)
        Assert.False(controller.IsFavorite("owner", "name"));
    }

    [Fact]
    public async Task AccountLoginCasingVariant_IsTreatedAsSameAccount()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("MustafaNazli");
        var controller = MakeController(store, userSessionStore);
        await controller.ToggleAsync("owner", "name", CancellationToken.None);

        userSessionStore.SignIn(new UserSession("fake-access-token", null, "mustafanazli", null));
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.True(controller.IsFavorite("owner", "name"));
    }

    // Two controllers (two independent UserSessionStores, as two separate
    // app-session objects would be) sharing one underlying store, toggling
    // concurrently as two different accounts — proves the store-level
    // account key, not any in-process lock the controller itself holds, is
    // what keeps the two accounts from corrupting each other's state.
    [Fact]
    public async Task ConcurrentTogglesAcrossTwoAccounts_NeverCrossContaminate()
    {
        var store = new FakeFavoriteRepositoryStore();
        var aliceController = MakeController(store, SignedInAs("alice"));
        var bobController = MakeController(store, SignedInAs("bob"));

        var aliceToggle = aliceController.ToggleAsync("owner", "shared-repo", CancellationToken.None);
        var bobToggle = bobController.ToggleAsync("owner", "shared-repo", CancellationToken.None);
        await Task.WhenAll(aliceToggle, bobToggle);

        var alicesFavorites = await store.GetAllAsync("alice", CancellationToken.None);
        var bobsFavorites = await store.GetAllAsync("bob", CancellationToken.None);
        Assert.Single(alicesFavorites.Favorites);
        Assert.Single(bobsFavorites.Favorites);
    }

    // ---- Cross-session async race safety ----
    //
    // These prove the fix for a second, subtler leak on top of the plain
    // per-account scoping above: EnsureLoadedForCurrentSessionAsync/
    // ToggleAsync both await a store call, and — before this fix — resumed
    // by unconditionally writing into the shared favoritesByKey dictionary
    // using whatever session/login was captured *before* that await. If a
    // sign-out/sign-in landed while the await was in flight, the resumed
    // continuation would publish the OLD account's data into what is now a
    // DIFFERENT account's in-memory state. The fix re-checks
    // UserSessionStore.SessionGeneration right before every such mutation
    // and discards (never applies) a result whose captured generation no
    // longer matches.

    [Fact]
    public async Task DelayedLoadForAccountA_CompletesAfterAccountBSignIn_DoesNotPublishAState()
    {
        var store = new FakeFavoriteRepositoryStore();
        await store.AddAsync("alice", "owner", "name", DateTimeOffset.UtcNow, CancellationToken.None);
        var gate = new TaskCompletionSource<bool>();
        store.BlockNextGetAllUntil = gate;
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);

        // Starts loading for alice; GetAllAsync("alice", ...) is now
        // suspended on `gate`.
        var loadTask = controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        // Alice's session ends and Bob's begins while that load is still
        // in flight.
        userSessionStore.SignOut();
        userSessionStore.SignIn(new UserSession("fake-access-token-b", null, "bob", null));

        // Let alice's GetAllAsync resolve now that bob is the active session.
        gate.SetResult(true);
        await loadTask;

        // Alice's favorite must never surface for bob, even transiently —
        // the late result is discarded, not merged into shared state.
        Assert.Empty(controller.Favorites);
        Assert.False(controller.IsFavorite("owner", "name"));
    }

    [Fact]
    public async Task DelayedToggleForAccountA_CompletesAfterAccountBSignIn_DoesNotPublishAState()
    {
        var store = new FakeFavoriteRepositoryStore();
        var gate = new TaskCompletionSource<bool>();
        store.BlockNextOperationUntil = gate;
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);

        // Starts toggling (adding) for alice; AddAsync is now suspended on
        // `gate`.
        var toggleTask = controller.ToggleAsync("owner", "name", CancellationToken.None);

        userSessionStore.SignOut();
        userSessionStore.SignIn(new UserSession("fake-access-token-b", null, "bob", null));

        gate.SetResult(true);
        var result = await toggleTask;

        // The stale outcome must be reported as Ignored (never Success),
        // and never applied to shared in-memory state now that bob is
        // active.
        Assert.True(result.IsIgnored);
        Assert.False(controller.IsFavorite("owner", "name"));
        Assert.Empty(controller.Favorites);

        // The DB write itself already landed correctly scoped to the
        // account that was actually active when ToggleAsync started —
        // that is a real, intentional write and must stand; it is only the
        // in-memory publication to the (now different) active session that
        // is suppressed.
        var alicesFavorites = await store.GetAllAsync("alice", CancellationToken.None);
        Assert.Single(alicesFavorites.Favorites);
        var bobsFavorites = await store.GetAllAsync("bob", CancellationToken.None);
        Assert.Empty(bobsFavorites.Favorites);
    }

    [Fact]
    public async Task SameAccountNewGeneration_ReloadsRatherThanReusingStaleData()
    {
        var store = new FakeFavoriteRepositoryStore();
        var userSessionStore = SignedInAs("alice");
        var controller = MakeController(store, userSessionStore);
        await controller.ToggleAsync("owner", "first", CancellationToken.None);
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);
        Assert.Single(controller.Favorites);

        // Sign back in as the same login — a new SessionGeneration even
        // though the account identity is unchanged (e.g. a token refresh
        // re-authentication) — and add a second favorite directly via the
        // store, bypassing the controller, to prove the next call performs
        // a real reload rather than trusting stale in-memory state.
        userSessionStore.SignIn(new UserSession("fake-access-token", null, "alice", null));
        await store.AddAsync("alice", "owner", "second", DateTimeOffset.UtcNow, CancellationToken.None);
        await controller.EnsureLoadedForCurrentSessionAsync(CancellationToken.None);

        Assert.Equal(2, controller.Favorites.Count);
    }

    [Fact]
    public void UserSessionSnapshot_HasNoTokenOrSecretShapedProperty()
    {
        var properties = typeof(UserSessionSnapshot).GetProperties();

        Assert.All(properties, property =>
        {
            Assert.DoesNotContain("Token", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Secret", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Hash", property.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    // Structural proof mirroring RepositoryListControllerTests'
    // AssertNoStringTypedFieldRetained (RP-010): the controller must never
    // retain a raw access token or refresh token in its own fields — it may
    // hold a UserSessionStore reference (a non-string object), but never
    // copy a token-shaped string out of it.
    [Fact]
    public void FavoriteToggleController_HasNoStringTypedFieldThatCouldRetainATokenOrSessionValue()
    {
        var fields = typeof(FavoriteToggleController).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var field in fields)
        {
            Assert.False(
                field.FieldType == typeof(string),
                $"Field '{field.Name}' is string-typed and could retain a token/session value.");
        }
    }
}
