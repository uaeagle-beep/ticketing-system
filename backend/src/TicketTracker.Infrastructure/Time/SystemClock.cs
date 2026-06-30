using TicketTracker.Application.Abstractions;

namespace TicketTracker.Infrastructure.Time;

/// <summary>Production clock — always UTC (ARCHITECTURE §3.3).</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
