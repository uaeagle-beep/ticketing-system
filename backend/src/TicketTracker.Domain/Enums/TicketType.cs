namespace TicketTracker.Domain.Enums;

/// <summary>
/// Ticket classification label. Canonical API values are lowercase: bug | feature | fix.
/// Stored as canonical lowercase text in the database (ARCHITECTURE §4.2).
/// </summary>
public enum TicketType
{
    Bug,
    Feature,
    Fix
}
