namespace TicketTracker.Application.Dtos;

// API_CONTRACT §7

public sealed record CreateCommentRequest(string? Body);

public sealed record CommentDto(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    string AuthorEmail,
    string Body,
    DateTime CreatedAt);
