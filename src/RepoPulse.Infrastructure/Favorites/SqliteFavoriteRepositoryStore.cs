using System.Collections.Concurrent;
using RepoPulse.Core.Repositories;
using SQLite;

namespace RepoPulse.Infrastructure.Favorites;

// RP-012's real, on-disk IFavoriteRepositoryStore. Every public method
// funnels through EnsureInitializedCoreAsync + a single path-keyed
// SemaphoreSlim (gate), so:
//   - schema initialization happens exactly once and can't race with itself,
//     even across two store instances that happen to point at the same file
//     (tests do exactly this to prove it);
//   - Add/Remove/GetAll/IsFavorite never interleave against the same file,
//     which is what makes AddAsync's "check, then insert" idempotency check
//     safe without needing a database-level upsert.
// No raw SQLite exception, message, or file path ever leaves this type —
// every failure is translated to a FavoriteStoreFailureKind before it
// crosses the IFavoriteRepositoryStore boundary.
//
// Every row is scoped by (AccountLoginNormalized, NormalizedFullName) — the
// composite identity that fixes the original version's cross-account
// favorite leak (a single global table shared by every GitHub account that
// ever signed in on the device). This PR has not merged yet, so schema
// version stays 1 (directly account-scoped) rather than adding a v1→v2
// migration for data that was never released; a stale pre-fix dev database
// is cleared by hand (uninstall/reinstall, or `pm clear`), never by
// destructive code in this store.
public sealed class SqliteFavoriteRepositoryStore : IFavoriteRepositoryStore, IAsyncDisposable
{
    internal const string TableName = "FavoriteRepositories";
    private const int CurrentSchemaVersion = 1;

    // Composite PRIMARY KEY (AccountLoginNormalized, NormalizedFullName) —
    // the same repository favorited by two different accounts is two
    // independent rows; the same account favoriting it twice (including a
    // casing-only repeat) is the same row. NOT NULL enforced on every
    // identity/data column. Table/column names here are fixed constants,
    // never interpolated from a variable — the only interpolation in this
    // file is CurrentSchemaVersion (an internal int constant, not data) into
    // the PRAGMA user_version statement, which SQLite does not support bound
    // parameters for.
    private const string CreateTableSql = $"""
        CREATE TABLE IF NOT EXISTS {TableName} (
            AccountLoginNormalized TEXT NOT NULL,
            NormalizedFullName TEXT NOT NULL,
            AccountLogin TEXT NOT NULL,
            Owner TEXT NOT NULL,
            Name TEXT NOT NULL,
            AddedAtUtc INTEGER NOT NULL,
            PRIMARY KEY (AccountLoginNormalized, NormalizedFullName)
        )
        """;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocksByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string databasePath;
    private readonly SemaphoreSlim gate;
    private SQLiteAsyncConnection? connection;
    private bool initialized;

    public SqliteFavoriteRepositoryStore(SqliteFavoriteRepositoryStoreOptions options)
    {
        databasePath = options.DatabasePath;
        gate = LocksByPath.GetOrAdd(databasePath, static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<FavoriteStoreResult> InitializeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureInitializedCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FavoriteListResult> GetAllAsync(string accountLogin, CancellationToken cancellationToken)
    {
        if (!FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccountLogin))
        {
            return FavoriteListResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var init = await EnsureInitializedCoreAsync().ConfigureAwait(false);
            if (!init.IsSuccess)
            {
                return FavoriteListResult.Failure(init.FailureKind!.Value);
            }

            var rows = await connection!.Table<FavoriteRepositoryRow>()
                .Where(row => row.AccountLoginNormalized == normalizedAccountLogin)
                .ToListAsync().ConfigureAwait(false);
            var favorites = rows
                .Select(row => new FavoriteRepository(row.Owner, row.Name, row.NormalizedFullName, DateTimeOffset.FromUnixTimeSeconds(row.AddedAtUtc)))
                .ToList();
            return FavoriteListResult.Success(favorites);
        }
        catch (SQLiteException ex)
        {
            return FavoriteListResult.Failure(ClassifySqliteException(ex));
        }
        catch (Exception)
        {
            return FavoriteListResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FavoriteStoreResult> AddAsync(string accountLogin, string owner, string name, DateTimeOffset addedAtUtc, CancellationToken cancellationToken)
    {
        if (!FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccountLogin) ||
            !FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity))
        {
            return FavoriteStoreResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var init = await EnsureInitializedCoreAsync().ConfigureAwait(false);
            if (!init.IsSuccess)
            {
                return init;
            }

            // Idempotent by construction: every Add/Remove/GetAll call for
            // this database file is serialized through `gate`, so this
            // check-then-insert can never race with another write to the
            // same file — a repeated Add for an already-favorited (account,
            // repository) pair always finds the existing row and leaves its
            // AddedAtUtc alone. A different account favoriting the exact
            // same repository is a separate row (separate composite key).
            var existing = await connection!.Table<FavoriteRepositoryRow>()
                .Where(row => row.AccountLoginNormalized == normalizedAccountLogin && row.NormalizedFullName == identity.NormalizedFullName)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (existing is not null)
            {
                return FavoriteStoreResult.Success();
            }

            var row = new FavoriteRepositoryRow
            {
                AccountLoginNormalized = normalizedAccountLogin,
                NormalizedFullName = identity.NormalizedFullName,
                AccountLogin = accountLogin.Trim(),
                Owner = identity.Owner,
                Name = identity.Name,
                AddedAtUtc = addedAtUtc.ToUnixTimeSeconds()
            };

            try
            {
                await connection.InsertAsync(row).ConfigureAwait(false);
            }
            catch (SQLiteException ex) when (ex.Result == SQLite3.Result.Constraint)
            {
                // Defensive-only: the `gate` above already makes this
                // unreachable in practice, but a PRIMARY KEY conflict here
                // still means "already a favorite for this account" rather
                // than a real failure, so it is treated the same as the
                // existing-row case above instead of surfacing as
                // Unexpected.
            }

            return FavoriteStoreResult.Success();
        }
        catch (SQLiteException ex)
        {
            return FavoriteStoreResult.Failure(ClassifySqliteException(ex));
        }
        catch (Exception)
        {
            return FavoriteStoreResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FavoriteStoreResult> RemoveAsync(string accountLogin, string owner, string name, CancellationToken cancellationToken)
    {
        if (!FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccountLogin) ||
            !FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity))
        {
            return FavoriteStoreResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var init = await EnsureInitializedCoreAsync().ConfigureAwait(false);
            if (!init.IsSuccess)
            {
                return init;
            }

            // Parameterized via the typed query API — never string
            // interpolation/concatenation of the identity into raw SQL.
            // Scoped to this account only, so removing a favorite can never
            // touch another account's row for the same repository.
            await connection!.Table<FavoriteRepositoryRow>()
                .Where(row => row.AccountLoginNormalized == normalizedAccountLogin && row.NormalizedFullName == identity.NormalizedFullName)
                .DeleteAsync().ConfigureAwait(false);

            return FavoriteStoreResult.Success();
        }
        catch (SQLiteException ex)
        {
            return FavoriteStoreResult.Failure(ClassifySqliteException(ex));
        }
        catch (Exception)
        {
            return FavoriteStoreResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FavoriteStatusResult> IsFavoriteAsync(string accountLogin, string owner, string name, CancellationToken cancellationToken)
    {
        if (!FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out var normalizedAccountLogin) ||
            !FavoriteRepositoryIdentifier.TryNormalize(owner, name, out var identity))
        {
            return FavoriteStatusResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var init = await EnsureInitializedCoreAsync().ConfigureAwait(false);
            if (!init.IsSuccess)
            {
                return FavoriteStatusResult.Failure(init.FailureKind!.Value);
            }

            var existing = await connection!.Table<FavoriteRepositoryRow>()
                .Where(row => row.AccountLoginNormalized == normalizedAccountLogin && row.NormalizedFullName == identity.NormalizedFullName)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            return FavoriteStatusResult.Success(existing is not null);
        }
        catch (SQLiteException ex)
        {
            return FavoriteStatusResult.Failure(ClassifySqliteException(ex));
        }
        catch (Exception)
        {
            return FavoriteStatusResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }
        finally
        {
            gate.Release();
        }
    }

    // Must only ever be called while holding `gate`. Idempotent: a second
    // (or concurrent, serialized-by-`gate`) call after `initialized` is
    // already true is a pure no-op success — this is what makes it safe for
    // every public method to call it unconditionally rather than requiring
    // callers to call InitializeAsync first.
    private async Task<FavoriteStoreResult> EnsureInitializedCoreAsync()
    {
        if (initialized)
        {
            return FavoriteStoreResult.Success();
        }

        try
        {
            connection ??= new SQLiteAsyncConnection(
                databasePath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

            var currentVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

            if (currentVersion > CurrentSchemaVersion)
            {
                // Never touch a database written by a newer version of this
                // app — no destructive recreate, no silent downgrade.
                return FavoriteStoreResult.Failure(FavoriteStoreFailureKind.UnsupportedSchema);
            }

            if (currentVersion == 0)
            {
                // Both statements commit together or not at all — a crash
                // between CREATE TABLE and setting user_version can never
                // leave a table with no version marker (which would look
                // like "no schema yet" again on next start — safe — but is
                // still avoided for cleanliness) or a version marker with no
                // table.
                await connection.RunInTransactionAsync(syncConnection =>
                {
                    syncConnection.Execute(CreateTableSql);
                    syncConnection.Execute($"PRAGMA user_version = {CurrentSchemaVersion}");
                }).ConfigureAwait(false);
            }

            initialized = true;
            return FavoriteStoreResult.Success();
        }
        catch (SQLiteException ex)
        {
            return FavoriteStoreResult.Failure(ClassifySqliteException(ex));
        }
        catch (Exception)
        {
            return FavoriteStoreResult.Failure(FavoriteStoreFailureKind.Unexpected);
        }
    }

    // SQLite's native result codes, translated to the four buckets
    // IFavoriteRepositoryStore callers are allowed to see. Corrupt/NonDBFile
    // are the two shapes SQLite reports for "this file's bytes are not a
    // valid database"; everything else SQLite-level (can't open, I/O,
    // permissions, full disk, locked, ...) is IoError.
    private static FavoriteStoreFailureKind ClassifySqliteException(SQLiteException ex) => ex.Result switch
    {
        SQLite3.Result.Corrupt => FavoriteStoreFailureKind.Corrupt,
        SQLite3.Result.NonDBFile => FavoriteStoreFailureKind.Corrupt,
        _ => FavoriteStoreFailureKind.IoError
    };

    // Test-only cleanup hook (production holds this store for the app's
    // entire lifetime and never disposes it) — releases the underlying file
    // handle so a temp-file-based test can delete its database afterward
    // without a "file in use" failure.
    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}
