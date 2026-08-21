using System.Security.Cryptography;
using System.Text;

namespace RepoPulse.Core.Authentication;

// Manages a single, in-memory, time-limited, single-use PKCE authorization
// session. There is never more than one pending session at a time: starting a
// new one while another is still pending and unexpired is rejected, and a
// session can be consumed (matched against the callback's state) at most once.
public sealed class AuthorizationSessionStore
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private AuthorizationSession? _pending;
    private bool _consumed;

    public AuthorizationSessionStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // False means a concurrent authorization is already in flight; callers
    // must reject the new sign-in attempt rather than overwrite the pending one.
    public bool TryStart(TimeSpan lifetime, out AuthorizationSession session)
    {
        lock (_gate)
        {
            if (_pending is not null && !_consumed && _pending.ExpiresAtUtc > _timeProvider.GetUtcNow())
            {
                session = null!;
                return false;
            }

            var verifier = PkceGenerator.CreateCodeVerifier();
            var newSession = new AuthorizationSession
            {
                State = PkceGenerator.CreateState(),
                CodeVerifier = verifier,
                CodeChallenge = PkceGenerator.CreateCodeChallenge(verifier),
                ExpiresAtUtc = _timeProvider.GetUtcNow().Add(lifetime)
            };

            _pending = newSession;
            _consumed = false;
            session = newSession;
            return true;
        }
    }

    // Validates the callback's state against the pending session: must exist,
    // not be expired, not already consumed, and match via constant-time compare.
    // On any failure the session is left untouched (still consumable exactly
    // once more only if it was never validly matched) except for the expiry
    // check, which never re-arms an expired session.
    public bool TryConsume(string? callbackState, out AuthorizationSession? session)
    {
        lock (_gate)
        {
            session = null;

            if (string.IsNullOrEmpty(callbackState) || _pending is null || _consumed)
            {
                return false;
            }

            if (_pending.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            {
                return false;
            }

            if (!StatesMatch(callbackState, _pending.State))
            {
                return false;
            }

            _consumed = true;
            session = _pending;
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pending = null;
            _consumed = false;
        }
    }

    private static bool StatesMatch(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
