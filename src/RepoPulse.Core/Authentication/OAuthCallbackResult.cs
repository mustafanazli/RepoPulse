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

    public static OAuthCallbackResult Cancelled(string? error, string? errorDescription) =>
        new(OAuthCallbackOutcome.Cancelled, code: null, state: null, error, errorDescription);

    public static OAuthCallbackResult Invalid(string? error = null, string? errorDescription = null) =>
        new(OAuthCallbackOutcome.Invalid, code: null, state: null, error, errorDescription);
}
