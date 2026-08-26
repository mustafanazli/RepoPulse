using RepoPulse.Core.Repositories;
using RepoPulse.Infrastructure.Favorites;
using SQLite;

namespace RepoPulse.UnitTests;

// Integration tests against a REAL SQLite file (no fakes/mocks) — each test
// gets its own fresh temp directory (never a shared/broad location) that is
// deleted, and only that directory, in DisposeAsync. Nothing here ever
// touches the real on-device database path (that's MauiProgram's job, at
// AppDataDirectory/repopulse.db3) or any file outside its own temp
// directory.
public class SqliteFavoriteRepositoryStoreTests : IAsyncLifetime
{
    private const string Account = "test-account";

    private string tempDirectory = string.Empty;

    private string DatabasePath => Path.Combine(tempDirectory, "favorites.db3");

    public Task InitializeAsync()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "RepoPulseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort only: a test that deliberately leaves a connection
            // open (rare, and always closed again before the test ends in
            // practice) must never fail the whole run over cleanup.
        }

        return Task.CompletedTask;
    }

    private SqliteFavoriteRepositoryStore CreateStore(string? path = null) =>
        new(new SqliteFavoriteRepositoryStoreOptions(path ?? DatabasePath));

    private static int ReadUserVersionDirectly(string path)
    {
        using var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly);
        var version = connection.ExecuteScalar<int>("PRAGMA user_version");
        connection.Close();
        return version;
    }

    [Fact]
    public async Task InitializeAsync_FreshDatabase_Succeeds()
    {
        await using var store = CreateStore();

        var result = await store.InitializeAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task InitializeAsync_FreshDatabase_SetsPragmaUserVersionToOne()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);
        await store.DisposeAsync();

        Assert.Equal(1, ReadUserVersionDirectly(DatabasePath));
    }

    [Fact]
    public async Task AddAsync_ThenGetAllAsync_ReturnsTheAddedFavorite()
    {
        await using var store = CreateStore();
        var addedAtUtc = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

        var addResult = await store.AddAsync(Account, "mustafanazli", "RepoPulse", addedAtUtc, CancellationToken.None);
        var listResult = await store.GetAllAsync(Account, CancellationToken.None);

        Assert.True(addResult.IsSuccess);
        Assert.True(listResult.IsSuccess);
        var favorite = Assert.Single(listResult.Favorites);
        Assert.Equal("mustafanazli", favorite.Owner);
        Assert.Equal("RepoPulse", favorite.Name);
        Assert.Equal("mustafanazli/repopulse", favorite.NormalizedFullName);
        Assert.Equal(addedAtUtc, favorite.AddedAtUtc);
    }

    [Fact]
    public async Task IsFavoriteAsync_AfterAdd_ReturnsTrue_AndFalseForUnrelatedRepository()
    {
        await using var store = CreateStore();
        await store.AddAsync(Account, "owner", "A", DateTimeOffset.UtcNow, CancellationToken.None);

        var isFavoriteA = await store.IsFavoriteAsync(Account, "owner", "A", CancellationToken.None);
        var isFavoriteB = await store.IsFavoriteAsync(Account, "owner", "B", CancellationToken.None);

        Assert.True(isFavoriteA.IsSuccess);
        Assert.True(isFavoriteA.IsFavorite);
        Assert.True(isFavoriteB.IsSuccess);
        Assert.False(isFavoriteB.IsFavorite);
    }

    [Fact]
    public async Task RemoveAsync_ExistingFavorite_DeletesIt()
    {
        await using var store = CreateStore();
        await store.AddAsync(Account, "owner", "A", DateTimeOffset.UtcNow, CancellationToken.None);

        var removeResult = await store.RemoveAsync(Account, "owner", "A", CancellationToken.None);
        var isFavorite = await store.IsFavoriteAsync(Account, "owner", "A", CancellationToken.None);
        var all = await store.GetAllAsync(Account, CancellationToken.None);

        Assert.True(removeResult.IsSuccess);
        Assert.False(isFavorite.IsFavorite);
        Assert.Empty(all.Favorites);
    }

    [Fact]
    public async Task RemoveAsync_NonExistentFavorite_IsANoOpSuccess()
    {
        await using var store = CreateStore();

        var result = await store.RemoveAsync(Account, "owner", "never-added", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RemoveAsync_CaseInsensitiveIdentity_RemovesRegardlessOfCasingDifference()
    {
        await using var store = CreateStore();
        await store.AddAsync(Account, "mustafanazli", "RepoPulse", DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await store.RemoveAsync(Account, "MustafaNazli", "repopulse", CancellationToken.None);
        var all = await store.GetAllAsync(Account, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(all.Favorites);
    }

    [Fact]
    public async Task AddAsync_CasingVariantOfExistingFavorite_DoesNotCreateASecondRow()
    {
        await using var store = CreateStore();
        await store.AddAsync(Account, "mustafanazli", "RepoPulse", DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await store.AddAsync(Account, "MustafaNazli", "repopulse", DateTimeOffset.UtcNow, CancellationToken.None);
        var all = await store.GetAllAsync(Account, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(all.Favorites);
    }

    [Fact]
    public async Task AddAsync_RepeatedAddForSameIdentity_PreservesOriginalAddedAtUtc()
    {
        await using var store = CreateStore();
        var originalAddedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.AddAsync(Account, "owner", "A", originalAddedAt, CancellationToken.None);

        var laterAddedAt = originalAddedAt.AddDays(30);
        await store.AddAsync(Account, "owner", "A", laterAddedAt, CancellationToken.None);

        var all = await store.GetAllAsync(Account, CancellationToken.None);
        var favorite = Assert.Single(all.Favorites);
        Assert.Equal(originalAddedAt, favorite.AddedAtUtc);
    }

    [Fact]
    public async Task CloseAndReopen_PersistsFavoritesAcrossConnections()
    {
        var firstStore = CreateStore();
        await firstStore.AddAsync(Account, "owner", "Persisted", DateTimeOffset.UtcNow, CancellationToken.None);
        await firstStore.DisposeAsync();

        await using var secondStore = CreateStore();
        var all = await secondStore.GetAllAsync(Account, CancellationToken.None);

        var favorite = Assert.Single(all.Favorites);
        Assert.Equal("Persisted", favorite.Name);
    }

    // Two independent store instances constructed with the identical path
    // share the same process-wide lock (SqliteFavoriteRepositoryStore keys
    // it by path) — concurrent InitializeAsync calls across both instances
    // must not race, corrupt the schema, or throw.
    [Fact]
    public async Task InitializeAsync_ConcurrentAcrossTwoInstancesOnSameFile_BothSucceedWithConsistentSchema()
    {
        await using var storeA = CreateStore();
        await using var storeB = CreateStore();

        var taskA = storeA.InitializeAsync(CancellationToken.None);
        var taskB = storeB.InitializeAsync(CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);
        Assert.Equal(1, ReadUserVersionDirectly(DatabasePath));
    }

    // Same setup, but both instances race to Add the exact same (account,
    // repository) identity — the shared lock must serialize them so the
    // result is exactly one row, never a duplicate and never an unhandled
    // constraint-violation exception.
    [Fact]
    public async Task AddAsync_ConcurrentDuplicateAddAcrossTwoInstances_ResultsInExactlyOneRow()
    {
        await using var storeA = CreateStore();
        await using var storeB = CreateStore();

        var taskA = storeA.AddAsync(Account, "owner", "Race", DateTimeOffset.UtcNow, CancellationToken.None);
        var taskB = storeB.AddAsync(Account, "owner", "Race", DateTimeOffset.UtcNow, CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);

        var all = await storeA.GetAllAsync(Account, CancellationToken.None);
        Assert.Single(all.Favorites);
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedFutureSchemaVersion_FailsWithoutModifyingTheDatabase()
    {
        // Simulates a database last written by a hypothetical future app
        // version: create the table for real, then set user_version far
        // beyond anything this build knows about.
        await using (var seedStore = CreateStore())
        {
            await seedStore.InitializeAsync(CancellationToken.None);
        }

        using (var raw = new SQLiteConnection(DatabasePath))
        {
            raw.Execute("PRAGMA user_version = 999");
            raw.Close();
        }

        await using var store = CreateStore();
        var result = await store.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FavoriteStoreFailureKind.UnsupportedSchema, result.FailureKind);
        // Never downgraded/recreated — the marker this test set is exactly
        // what a real future-version database would still show.
        Assert.Equal(999, ReadUserVersionDirectly(DatabasePath));
    }

    [Fact]
    public async Task InitializeAsync_CorruptDatabaseFile_FailsWithTypedFailureNeverThrowing()
    {
        await File.WriteAllBytesAsync(DatabasePath, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        await using var store = CreateStore();
        var result = await store.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(
            result.FailureKind is FavoriteStoreFailureKind.Corrupt or FavoriteStoreFailureKind.IoError,
            $"Expected Corrupt or IoError, got {result.FailureKind}.");
    }

    [Fact]
    public async Task InitializeAsync_UnwritableParentDirectory_FailsWithIoError()
    {
        var pathWithMissingDirectory = Path.Combine(tempDirectory, "does-not-exist", "favorites.db3");
        await using var store = CreateStore(pathWithMissingDirectory);

        var result = await store.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FavoriteStoreFailureKind.IoError, result.FailureKind);
    }

    // The core distinction the RP-012 brief calls out explicitly: a
    // genuinely empty favorites list (IsSuccess=true) must never be
    // confusable with a database that could not be read (IsSuccess=false).
    [Fact]
    public async Task GetAllAsync_EmptyDatabaseVsCorruptDatabase_AreDistinguishable()
    {
        await using var emptyStore = CreateStore();
        var emptyResult = await emptyStore.GetAllAsync(Account, CancellationToken.None);

        var corruptPath = Path.Combine(tempDirectory, "corrupt.db3");
        await File.WriteAllBytesAsync(corruptPath, [0xFF, 0xFE, 0xFD, 0xFC]);
        await using var corruptStore = CreateStore(corruptPath);
        var corruptResult = await corruptStore.GetAllAsync(Account, CancellationToken.None);

        Assert.True(emptyResult.IsSuccess);
        Assert.Empty(emptyResult.Favorites);

        Assert.False(corruptResult.IsSuccess);
        Assert.NotNull(corruptResult.FailureKind);
        Assert.Empty(corruptResult.Favorites);
    }

    // ---- Account isolation (fix for the cross-account favorite leak) ----

    [Fact]
    public async Task GetAllAsync_FavoriteAddedByAccountA_IsNotReturnedForAccountB()
    {
        await using var store = CreateStore();
        await store.AddAsync("alice", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);

        var bobsFavorites = await store.GetAllAsync("bob", CancellationToken.None);

        Assert.True(bobsFavorites.IsSuccess);
        Assert.Empty(bobsFavorites.Favorites);
    }

    [Fact]
    public async Task AddAsync_SameRepository_CanBeFavoritedIndependentlyByTwoAccounts()
    {
        await using var store = CreateStore();

        var aliceResult = await store.AddAsync("alice", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);
        var bobResult = await store.AddAsync("bob", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(aliceResult.IsSuccess);
        Assert.True(bobResult.IsSuccess);
        Assert.Single((await store.GetAllAsync("alice", CancellationToken.None)).Favorites);
        Assert.Single((await store.GetAllAsync("bob", CancellationToken.None)).Favorites);
    }

    [Fact]
    public async Task RemoveAsync_AccountBRemovingSharedRepository_DoesNotRemoveAccountAsRow()
    {
        await using var store = CreateStore();
        await store.AddAsync("alice", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);
        await store.AddAsync("bob", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);

        var removeResult = await store.RemoveAsync("bob", "owner", "shared-repo", CancellationToken.None);

        Assert.True(removeResult.IsSuccess);
        Assert.Empty((await store.GetAllAsync("bob", CancellationToken.None)).Favorites);
        var alicesFavorite = Assert.Single((await store.GetAllAsync("alice", CancellationToken.None)).Favorites);
        Assert.Equal("shared-repo", alicesFavorite.Name);
    }

    [Fact]
    public async Task IsFavoriteAsync_ScopedPerAccount_TrueForOwnerFalseForOther()
    {
        await using var store = CreateStore();
        await store.AddAsync("alice", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);

        var aliceStatus = await store.IsFavoriteAsync("alice", "owner", "shared-repo", CancellationToken.None);
        var bobStatus = await store.IsFavoriteAsync("bob", "owner", "shared-repo", CancellationToken.None);

        Assert.True(aliceStatus.IsFavorite);
        Assert.False(bobStatus.IsFavorite);
    }

    [Fact]
    public async Task AddAsync_AccountLoginCasingVariant_IsTreatedAsSameAccount_NoDuplicateRow()
    {
        await using var store = CreateStore();
        await store.AddAsync("MustafaNazli", "owner", "name", DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await store.AddAsync("mustafanazli", "owner", "name", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single((await store.GetAllAsync("MUSTAFANAZLI", CancellationToken.None)).Favorites);
    }

    // Concurrent writes from two DIFFERENT accounts against the shared
    // database file (same path, same process-wide gate) must never corrupt
    // or cross-contaminate each other's rows — this is the composite-key
    // isolation guarantee under real concurrency, not just sequential calls.
    [Fact]
    public async Task ConcurrentAddsAcrossTwoDifferentAccounts_NeverCrossContaminate()
    {
        await using var storeA = CreateStore();
        await using var storeB = CreateStore();

        var taskAlice = storeA.AddAsync("alice", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);
        var taskBob = storeB.AddAsync("bob", "owner", "shared-repo", DateTimeOffset.UtcNow, CancellationToken.None);
        var results = await Task.WhenAll(taskAlice, taskBob);

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);
        Assert.Single((await storeA.GetAllAsync("alice", CancellationToken.None)).Favorites);
        Assert.Single((await storeA.GetAllAsync("bob", CancellationToken.None)).Favorites);
    }

    // Token/secret leak proof, at the actual persisted-row shape level: the
    // internal sqlite-net-pcl row type must never grow a property that
    // could hold a token, refresh token, or any kind of hash/secret — only
    // account/repository identity + AddedAtUtc.
    [Fact]
    public void FavoriteRepositoryRow_HasNoTokenOrSecretShapedProperty()
    {
        var propertyNames = typeof(FavoriteRepositoryRow)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        var allowed = new[] { "AccountLoginNormalized", "NormalizedFullName", "AccountLogin", "Owner", "Name", "AddedAtUtc" };
        Assert.Equal(allowed.Length, propertyNames.Length);
        Assert.All(propertyNames, name => Assert.Contains(name, allowed));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }
}
