namespace TicketTracker.Domain.Enums;

/// <summary>
/// Kanban workflow state. Canonical API values (workflow order):
/// new | ready_for_implementation | in_progress | ready_for_acceptance | done.
/// Stored as canonical lowercase text in the database (ARCHITECTURE §4.2).
/// The enum ordinal order matches the workflow order used to render board columns.
/// </summary>
public enum TicketState
{
    New,
    ReadyForImplementation,
    InProgress,
    ReadyForAcceptance,
    Done
}
