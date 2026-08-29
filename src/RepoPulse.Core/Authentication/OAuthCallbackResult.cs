namespace RepoPulse.Core.Authentication;

public sealed class OAuthCallbackResult
{
    public OAuthCallbackOutcome Outcome { get; }
    public string? Code { get; }
    public string? State { get; }
    public string? Error { get; }
    public string? ErrorDescription { get; }

    private OAuthCallbackResult(OAuthCallbackOutcome outcome, string? code, string? state, string? error, string? errorDescription)
    {
        Outcome = outcome;
        Code = code;
        State = state;
        Error = error;
        ErrorDescription = errorDescription;
    }

    public static OAuthCallbackResult Success(string code, string state) =>
        new(OAuthCallbackOutcome.Success, code, state, error: null, errorDescription: null);

    // `state` is optional here (unlike Success, where GitHub's redirect always
    // carries the one we sent) because a cancellation/error redirect can only
    // be validated against the pending session when GitHub actually echoed it
    // back — see OAuthCallbackAttemptGate for how a present-but-unvalidated
    // state is handled.
    public static OAuthCallbackResult Cancelled(string? error, string? errorDescription, string? state = null) =>
        new(OAuthCallbackOutcome.Cancelled, code: null, state, error, errorDescription);

    public static OAuthCallbackResult Invalid(string? error = null, string? errorDescription = null, string? state = null) =>
        new(OAuthCallbackOutcome.Invalid, code: null, state, error, errorDescription);
}
