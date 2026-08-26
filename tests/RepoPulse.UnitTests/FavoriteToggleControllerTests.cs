using System.Reflection;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// No test here ever touches a real database — FakeFavoriteRepositoryStore
// drives every scenario, so these prove FavoriteToggleController's own
// logic (idempotent toggle, double-tap guard, failure surfacing) in
// isolation from SqliteFavoriteRepositoryStore (covered separately, against
// a real temp-file database, in SqliteFavoriteRepositoryStoreTests).
public class FavoriteToggleControllerTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeFavoriteRepositoryStore : IFavoriteRepositoryStore
    {
        private readonly Dictionary<string, FavoriteRepository> favorites = new(StringComparer.Ordinal);

        public FavoriteStoreFailureKind? FailNextOperation { get; set; }
        public int AddCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }
        public TaskCompletionSource<bool>? BlockNextOperationUntil { get; set; }

        public async Task<FavoriteStoreResult> InitializeAsync(CancellationToken cancellationToken) =>
            await Task.FromResult(FavoriteStoreResult.Success());

        public Task<FavoriteListResult> GetAllAsync(CancellationToken cancellationToken)
        {
            if (FailNextOperation is { } kind)
            {
                FailNextOperation = null;
                return Task.FromResult(FavoriteListResult.Failure(kind));
            }

            return Task.FromResult(FavoriteListResult.Success(favorites.Values.ToList()));
        }

        public async Task<FavoriteStoreResult> AddAsync(string owner, string name, DateTimeOffset addedAtUtc, CancellationToken cancellationToken)
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

            FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
            favorites.TryAdd(identity.NormalizedFullName, new FavoriteRepository(identity.Owner, identity.Name, identity.NormalizedFullName, addedAtUtc));
            return FavoriteStoreResult.Success();
        }

        public Task<FavoriteStoreResult> RemoveAsync(string owner, string name, CancellationToken cancellationToken)
        {
            RemoveCallCount++;
            if (FailNextOperation is { } kind)
            {
                FailNextOperation = null;
                return Task.FromResult(FavoriteStoreResult.Failure(kind));
            }

            FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
            favorites.Remove(identity.NormalizedFullName);
            return Task.FromResult(FavoriteStoreResult.Success());
        }

        public Task<FavoriteStatusResult> IsFavoriteAsync(string owner, string name, CancellationToken cancellationToken)
        {
            FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity);
            return Task.FromResult(FavoriteStatusResult.Success(favorites.ContainsKey(identity.NormalizedFullName)));
        }
    }

    [Fact]
    public async Task ToggleAsync_NotYetFavorite_AddsAndReportsFavorite()
    {
        var store = new FakeFavoriteRepositoryStore();
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));

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
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));
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
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));
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
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));

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
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));

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
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));

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
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));
        await controller.ToggleAsync("owner", "name", CancellationToken.None);

        store.FailNextOperation = FavoriteStoreFailureKind.Corrupt;
        var result = await controller.ToggleAsync("owner", "name", CancellationToken.None);

        Assert.False(result.IsSuccess);
        // Still a favorite — the failed remove must not have taken effect.
        Assert.True(controller.IsFavorite("owner", "name"));
    }

    [Fact]
    public async Task LoadAsync_StoreFailure_SetsLastLoadFailureAndKeepsFavoritesEmpty()
    {
        var store = new FakeFavoriteRepositoryStore { FailNextOperation = FavoriteStoreFailureKind.Corrupt };
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));

        await controller.LoadAsync(CancellationToken.None);

        Assert.Equal(FavoriteStoreFailureKind.Corrupt, controller.LastLoadFailure);
        Assert.Empty(controller.Favorites);
    }

    [Fact]
    public async Task LoadAsync_Success_ClearsPreviousLoadFailure()
    {
        var store = new FakeFavoriteRepositoryStore { FailNextOperation = FavoriteStoreFailureKind.IoError };
        var controller = new FavoriteToggleController(store, new FixedTimeProvider(DateTimeOffset.UtcNow));
        await controller.LoadAsync(CancellationToken.None);
        Assert.NotNull(controller.LastLoadFailure);

        await controller.LoadAsync(CancellationToken.None);

        Assert.Null(controller.LastLoadFailure);
    }

    // Structural proof mirroring RepositoryListControllerTests'
    // AssertNoStringTypedFieldRetained (RP-010): favorites are intentionally
    // never tied to the GitHub session/access token, so no field on this
    // controller should ever be string-typed in a way that could carry one.
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
