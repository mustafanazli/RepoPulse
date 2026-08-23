namespace RepoPulse.Core.Repositories;

public sealed record RepositoryIdentifier(string Owner, string Name);

public sealed class RepositoryIdentifierParseResult
{
    public bool IsSuccess { get; }
    public RepositoryIdentifier? Value { get; }
    public string? SafeErrorMessage { get; }

    private RepositoryIdentifierParseResult(bool isSuccess, RepositoryIdentifier? value, string? safeErrorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        SafeErrorMessage = safeErrorMessage;
    }

    public static RepositoryIdentifierParseResult Success(RepositoryIdentifier value) => new(true, value, null);

    public static RepositoryIdentifierParseResult Failure(string safeErrorMessage) => new(false, null, safeErrorMessage);
}
