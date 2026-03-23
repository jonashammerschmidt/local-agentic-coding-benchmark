namespace LocalAgenticCodingBenchmark.Core;

public sealed class Clock
{
    public DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
}
