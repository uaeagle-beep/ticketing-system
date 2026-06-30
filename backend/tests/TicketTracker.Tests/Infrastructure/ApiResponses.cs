using System.Text.Json.Serialization;

namespace TicketTracker.Tests.Infrastructure;

// Loose-typed response DTOs for tests. Kept separate from the production DTOs so a test never
// silently passes by reusing the same record the controller serialized; we assert on the wire shape.

public sealed record ErrorEnvelopeDto([property: JsonPropertyName("error")] ErrorBodyDto Error);

public sealed record ErrorBodyDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("errors")] Dictionary<string, string[]>? Errors);

public sealed record MessageDto([property: JsonPropertyName("message")] string Message);

public sealed record UserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("emailVerified")] bool EmailVerified);

public sealed record LoginDto(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("user")] UserDto User,
    [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt);

public sealed record TeamDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ticketCount")] int TicketCount,
    [property: JsonPropertyName("epicCount")] int EpicCount,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("modifiedAt")] DateTime ModifiedAt,
    [property: JsonPropertyName("wipLimits")] Dictionary<string, int?>? WipLimits = null);

public sealed record EpicDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("teamId")] Guid TeamId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("ticketCount")] int TicketCount,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("modifiedAt")] DateTime ModifiedAt);

public sealed record TicketDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("teamId")] Guid TeamId,
    [property: JsonPropertyName("epicId")] Guid? EpicId,
    [property: JsonPropertyName("epicTitle")] string? EpicTitle,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("modifiedAt")] DateTime ModifiedAt,
    [property: JsonPropertyName("createdBy")] Guid CreatedBy,
    [property: JsonPropertyName("createdByEmail")] string CreatedByEmail);

public sealed record TicketCardDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("epicId")] Guid? EpicId,
    [property: JsonPropertyName("epicTitle")] string? EpicTitle,
    [property: JsonPropertyName("modifiedAt")] DateTime ModifiedAt);

public sealed record BoardColumnDto(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("tickets")] List<TicketCardDto> Tickets,
    [property: JsonPropertyName("total")] int Total = 0,
    [property: JsonPropertyName("wipLimit")] int? WipLimit = null);

public sealed record BoardDto(
    [property: JsonPropertyName("teamId")] Guid TeamId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("columns")] List<BoardColumnDto> Columns);

public sealed record TicketStateDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("modifiedAt")] DateTime ModifiedAt);

public sealed record CommentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ticketId")] Guid TicketId,
    [property: JsonPropertyName("authorId")] Guid AuthorId,
    [property: JsonPropertyName("authorEmail")] string AuthorEmail,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt);
