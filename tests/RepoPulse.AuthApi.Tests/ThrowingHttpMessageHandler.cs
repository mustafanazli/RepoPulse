namespace RepoPulse.AuthApi.Tests;

// Simulates network failures / timeouts to the (fake) GitHub endpoint.
internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception exceptionToThrow;

    public ThrowingHttpMessageHandler(Exception exceptionToThrow)
    {
        this.exceptionToThrow = exceptionToThrow;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw exceptionToThrow;
}
