namespace RepoPulse.UnitTests;

// Hand-rolled instead of pulling in Microsoft.Extensions.TimeProvider.Testing —
// TimeProvider is a plain BCL abstract class, trivial to fake directly.
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now;

    public ManualTimeProvider(DateTimeOffset start) => now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
