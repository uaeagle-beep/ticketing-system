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

public sealed record TeamRefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record UserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("emailVerified")] bool EmailVerified,
    [property: JsonPropertyName("isAdmin")] bool IsAdmin = false,
    [property: JsonPropertyName("isBlocked")] bool IsBlocked = false,
    [property: JsonPropertyName("teams")] List<TeamRefDto>? Teams = null,
    [property: JsonPropertyName("name")] string? Name = null);

public sealed record AdminUserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("isAdmin")] bool IsAdmin,
    [property: JsonPropertyName("isBlocked")] bool IsBlocked,
    [property: JsonPropertyName("emailVerified")] bool EmailVerified,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("teams")] List<TeamRefDto> Teams,
    [property: JsonPropertyName("name")] string? Name = null);

public sealed record CreateUserResponseDto(
    [property: JsonPropertyName("user")] AdminUserDto User,
    [property: JsonPropertyName("generatedPassword")] string? GeneratedPassword);

public sealed record ResetPasswordResponseDto(
    [property: JsonPropertyName("generatedPassword")] string GeneratedPassword);

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

// A lightweight assignee reference mirror (id + display name) — Wave 1 F-02 (§4.2).
public sealed record AssigneeRefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string DisplayName);

// A label as returned by the label CRUD endpoints (Wave 2, §5.6, ADR-0016).
public sealed record LabelDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("teamId")] Guid TeamId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color")] string Color);

// A lightweight label reference (id + name + color) on tickets (Wave 2, §8.5).
public sealed record LabelRefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color")] string Color);

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
    [property: JsonPropertyName("createdByEmail")] string CreatedByEmail,
    [property: JsonPropertyName("createdByName")] string? CreatedByName = null,
    // Wave 1 additions (§4.1/§4.2/§4.3). Defaulted so existing positional call-sites keep compiling.
    [property: JsonPropertyName("priority")] string Priority = "medium",
    [property: JsonPropertyName("dueDate")] DateOnly? DueDate = null,
    [property: JsonPropertyName("isOverdue")] bool IsOverdue = false,
    [property: JsonPropertyName("assignees")] List<AssigneeRefDto>? Assignees = null,
    // Wave 2 (§6.7): whether the current user watches this ticket.
    [property: JsonPropertyName("isWatching")] bool IsWatching = false,
    // Wave 2 (§8.5, ADR-0016): the ticket's labels.
    [property: JsonPropertyName("labels")] List<LabelRefDto>? Labels = null);

public sealed record TicketCardDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("epicId")] Guid? EpicId,
    [property: JsonPropertyName("epicTitle")] string? EpicTitle,
    [property: JsonPropertyName("modifiedAt")] DateTime ModifiedAt,
    // Wave 1 additions on the board card (§4.1/§4.2/§4.3).
    [property: JsonPropertyName("priority")] string Priority = "medium",
    [property: JsonPropertyName("dueDate")] DateOnly? DueDate = null,
    [property: JsonPropertyName("isOverdue")] bool IsOverdue = false,
    [property: JsonPropertyName("assignees")] List<AssigneeRefDto>? Assignees = null,
    // Wave 2 (§8.5, ADR-0016): the card's labels.
    [property: JsonPropertyName("labels")] List<LabelRefDto>? Labels = null);

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
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("authorName")] string? AuthorName = null,
    // F-12 (WAVE2 §5.2): the comment's edit indicator + timestamp.
    [property: JsonPropertyName("edited")] bool Edited = false,
    [property: JsonPropertyName("editedAt")] DateTime? EditedAt = null);

// A team member for the member-visible picker (Wave-1 debt, WAVE2 §5.8 / ADR-0017).
public sealed record TeamMemberDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("isAdmin")] bool IsAdmin);

// ---------- Wave 2 notifications subsystem (§8) ----------

public sealed record NotificationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("ticketId")] Guid? TicketId,
    [property: JsonPropertyName("commentId")] Guid? CommentId,
    [property: JsonPropertyName("actorId")] Guid ActorId,
    [property: JsonPropertyName("actorDisplayName")] string ActorDisplayName,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("readAt")] DateTime? ReadAt);

public sealed record NotificationListDto(
    [property: JsonPropertyName("items")] List<NotificationDto> Items,
    [property: JsonPropertyName("unreadCount")] int UnreadCount,
    [property: JsonPropertyName("hasMore")] bool HasMore,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

public sealed record UnreadCountDto(
    [property: JsonPropertyName("unreadCount")] int UnreadCount);

public sealed record NotificationSettingsDto(
    [property: JsonPropertyName("emailNotificationsEnabled")] bool EmailNotificationsEnabled);

public sealed record ActivityEntryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("actorId")] Guid ActorId,
    [property: JsonPropertyName("actorDisplayName")] string ActorDisplayName,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt);

public sealed record ActivityListDto(
    [property: JsonPropertyName("items")] List<ActivityEntryDto> Items,
    [property: JsonPropertyName("hasMore")] bool HasMore,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

public sealed record WatcherRefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string DisplayName);

public sealed record WatchStatusDto(
    [property: JsonPropertyName("watching")] bool Watching);

public sealed record WatchersDto(
    [property: JsonPropertyName("watching")] bool Watching,
    [property: JsonPropertyName("watchers")] List<WatcherRefDto> Watchers);
