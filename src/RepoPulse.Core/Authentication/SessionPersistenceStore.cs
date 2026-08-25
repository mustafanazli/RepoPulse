namespace RepoPulse.Core.Authentication;

// Orchestrates SecureStorage persistence together with UserSessionStore
// (RP-008) so the two never drift apart: a save is only ever reflected in
// memory once it is durably persisted, and a sign-out clears the persisted
// copy before the in-memory one. All three operations share one gate, so a
// save/restore/logout in flight is never interleaved with another.
//
// Every SecureStorage call is wrapped here (never in the platform
// implementation) specifically so failure handling — including the known
// Android scenario where a backup/restore leaves an undecryptable value
// behind — is exercised by plain unit tests against a fake
// ISecureSessionStorage, without a running MAUI host.
public sealed class SessionPersistenceStore
{
    private readonly ISecureSessionStorage secureStorage;
    private readonly ISessionInvalidationMarker invalidationMarker;
    private readonly UserSessionStore userSessionStore;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SessionPersistenceStore(
        ISecureSessionStorage secureStorage,
        ISessionInvalidationMarker invalidationMarker,
        UserSessionStore userSessionStore)
    {
        this.secureStorage = secureStorage;
        this.invalidationMarker = invalidationMarker;
        this.userSessionStore = userSessionStore;
    }

    // Called once OAuth + GET /user have both already succeeded. Persists
    // first; UserSessionStore is only populated once that succeeds — a
    // sign-in is never considered complete on the strength of the in-memory
    // write alone. On any failure (validation or storage), the in-memory
    // store is (re)cleared and false is returned; the caller must show only
    // a generic, non-sensitive error and must not navigate onward.
    public async Task<bool> SignInAsync(UserSession session, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payload = new PersistedSessionPayload(
                PersistedSessionPayload.CurrentVersion,
                session.AccessToken,
                session.RefreshToken,
                session.Login,
                session.AvatarUrl,
                session.AccessTokenExpiresAtUtc);

            if (!PersistedSessionPayloadValidator.Validate(payload, DateTimeOffset.UtcNow, out _))
            {
                userSessionStore.SignOut();
                return false;
            }

            try
            {
                var json = PersistedSessionPayloadValidator.Serialize(payload);
                await secureStorage.SetAsync(json);
            }
            catch (Exception)
            {
                // Never leave a session in memory that could not be
                // durably persisted — otherwise a later restart would
                // silently sign the user back out anyway, but only after
                // they believed sign-in had fully succeeded this session.
                userSessionStore.SignOut();
                return false;
            }

            // A freshly, successfully persisted session supersedes any
            // earlier "removal could not be confirmed" marker from a prior
            // logout/401 — otherwise this brand-new, valid session would
            // itself be rejected by RestoreAsync on the very next cold
            // start.
            await TryClearInvalidationMarkerAsync();

            userSessionStore.SignIn(session);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    // Cold-start restore. Never makes a network call — only reads and
    // locally validates the stored payload — so a device with no
    // connectivity can still restore into RepositoryListPage; the next
    // actual API call is what surfaces a network error, not restore itself.
    public async Task<bool> RestoreAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            // The marker exists specifically for the case where a prior
            // logout/401 could not confirm the persisted session key was
            // actually removed — if set, whatever is still on disk (if
            // anything) must never be trusted again until a new sign-in
            // clears it. A failure reading the marker itself is treated
            // the same as "set" (fail closed) rather than silently trusting
            // a session that may have just been invalidated.
            bool invalidated;
            try
            {
                invalidated = await invalidationMarker.IsSetAsync();
            }
            catch (Exception)
            {
                invalidated = true;
            }

            if (invalidated)
            {
                await TryRemovePersistedAsync();
                return false;
            }

            string? raw;
            try
            {
                raw = await secureStorage.GetAsync();
            }
            catch (Exception)
            {
                // Covers, among other things, an Android value left
                // undecryptable by a backup/restore onto a different
                // device/keystore — never crash, just drop the app's own
                // session key and fall back to Login.
                await TryRemovePersistedAsync();
                return false;
            }

            if (raw is null)
            {
                return false;
            }

            if (!PersistedSessionPayloadValidator.TryParse(raw, utcNow, out var payload, out _) || payload is null)
            {
                await TryRemovePersistedAsync();
                return false;
            }

            userSessionStore.SignIn(new UserSession(
                payload.AccessToken!,
                payload.RefreshToken,
                payload.Login!,
                payload.AvatarUrl,
                payload.AccessTokenExpiresAtUtc));
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    // Removes the persisted key BEFORE clearing memory, and only clears
    // memory (and reports success) once that removal is confirmed — so a
    // failed removal never lets the caller claim sign-out succeeded while
    // the old session is still on disk waiting to be restored on next
    // launch. The caller must show a generic error and allow retry on
    // false, not silently treat the user as signed out.
    public async Task<bool> SignOutAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!await TryRemovePersistedAsync())
            {
                return false;
            }

            userSessionStore.SignOut();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    // True unless RemoveAsync itself threw — a `false`-shaped "key wasn't
    // there" outcome from the platform API still counts as success here,
    // since the end state (no persisted session) is what was asked for.
    private async Task<bool> TryRemovePersistedAsync()
    {
        try
        {
            await secureStorage.RemoveAsync();
            return true;
        }
        catch (Exception)
        {
            // Could not confirm the persisted session was actually
            // removed — set the non-sensitive invalidation marker so a
            // later RestoreAsync refuses to trust it even if the bytes are
            // still on disk. Best-effort: if even this write fails, there
            // is nothing further this method can safely do locally.
            await TrySetInvalidationMarkerAsync();
            return false;
        }
    }

    private async Task TrySetInvalidationMarkerAsync()
    {
        try
        {
            await invalidationMarker.SetAsync();
        }
        catch (Exception)
        {
        }
    }

    private async Task TryClearInvalidationMarkerAsync()
    {
        try
        {
            await invalidationMarker.ClearAsync();
        }
        catch (Exception)
        {
        }
    }
}
