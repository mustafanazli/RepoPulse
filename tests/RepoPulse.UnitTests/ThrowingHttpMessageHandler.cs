using System.Net.Http;

namespace RepoPulse.UnitTests;

// Simulates network failures / timeouts (as opposed to FakeHttpMessageHandler,
// which simulates real HTTP responses).
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
