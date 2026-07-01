namespace TicketTracker.Application.Dtos;

// API_CONTRACT §7

public sealed record CreateCommentRequest(string? Body);

/// <summary>Body of PUT /api/comments/{id} — the new comment text (F-12, WAVE2 §5.2).</summary>
public sealed record EditCommentRequest(string? Body);

public sealed record CommentDto(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    string AuthorEmail,
    string? AuthorName,
    string Body,
    DateTime CreatedAt,
    // F-12 (WAVE2 §5.2): Edited is true once the body has been edited; EditedAt is the edit timestamp (null otherwise).
    bool Edited = false,
    DateTime? EditedAt = null);
