namespace TicketTracker.Domain.Entities;

/// <summary>
/// An immutable note on a ticket (V24). Body non-empty after trim; author and createdAt
/// are server-set (V23, A20/A21). Deleting a ticket cascades to its comments (V22) — the
/// only mandated cascade. Adding a comment never touches the ticket's ModifiedAt (V21).
/// </summary>
public class Comment
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid AuthorId { get; set; }
    public User? Author { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
