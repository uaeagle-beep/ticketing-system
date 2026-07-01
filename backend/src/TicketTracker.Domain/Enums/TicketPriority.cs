namespace TicketTracker.Domain.Enums;

/// <summary>
/// Ticket priority. Canonical API values (ascending severity): low | medium | high | urgent.
/// Stored as canonical lowercase text with a DB CHECK, exactly like <see cref="TicketType"/>/
/// <see cref="TicketState"/> (ARCHITECTURE §4.2, ADR-0009). The enum ordinal order is ascending
/// severity and is used only for a stable badge — priority does NOT affect board ordering (A22).
/// New tickets default to <see cref="Medium"/>.
/// </summary>
public enum TicketPriority
{
    Low,
    Medium,
    High,
    Urgent
}
