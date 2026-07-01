namespace TicketTracker.Domain.Entities;

/// <summary>
/// A note on a ticket. Body non-empty after trim; author and createdAt are server-set
/// (V23, A20/A21). Deleting a ticket cascades to its comments (V22) — the only mandated cascade.
/// Adding a comment never touches the ticket's ModifiedAt (V21). Wave 2 (F-12) allows an author to
/// edit their own comment and an author-or-admin to delete it; an edit stamps <see cref="EditedAt"/>.
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

    /// <summary>
    /// When the body was last edited (F-12, WAVE2 §4.6). Null = never edited. A no-op edit
    /// (same normalized body) does NOT set this.
    /// </summary>
    public DateTime? EditedAt { get; set; }
}
