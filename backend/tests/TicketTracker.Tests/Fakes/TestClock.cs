using TicketTracker.Application.Abstractions;

namespace TicketTracker.Tests.Fakes;

/// <summary>
/// Controllable <see cref="IClock"/> for deterministic tests of modified_at semantics and
/// token-TTL / expiry boundaries (A19, A31). Defaults to a fixed UTC instant; tests advance it
/// explicitly so timestamp comparisons never depend on wall-clock resolution.
/// </summary>
public sealed class TestClock : IClock
{
    private DateTime _utcNow;

    public TestClock(DateTime? start = null)
        => _utcNow = start ?? new DateTime(2026, 06, 30, 12, 00, 00, DateTimeKind.Utc);

    public DateTime UtcNow => _utcNow;

    /// <summary>Set the clock to an explicit UTC instant.</summary>
    public void SetUtcNow(DateTime value) => _utcNow = DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>Advance the clock by the given amount and return the new instant.</summary>
    public DateTime Advance(TimeSpan by)
    {
        _utcNow = _utcNow.Add(by);
        return _utcNow;
    }
}
