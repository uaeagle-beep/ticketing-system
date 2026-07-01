namespace TicketTracker.Application.Dtos;

// API_CONTRACT §8 (Wave 2, ADR-0013). All Self-scoped (current user).

/// <summary>
/// One in-app notification. <c>ReadAt == null</c> means unread. <c>TicketId == null</c> is a
/// deleted-ticket tombstone (non-navigable in the SPA, §6.6).
/// </summary>
public sealed record NotificationDto(
    Guid Id,
    string EventType,
    string Summary,
    Guid? TicketId,
    Guid? CommentId,
    Guid ActorId,
    string ActorDisplayName,
    DateTime CreatedAt,
    DateTime? ReadAt);

/// <summary>
/// A page of notifications newest-first plus the caller's unread count (so the bell updates from the
/// same call). Keyset pagination via an opaque cursor of the last item's <c>(created_at, id)</c>.
/// </summary>
public sealed record NotificationListDto(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount,
    bool HasMore,
    string? NextCursor);

/// <summary>Cheap poll target for the bell badge.</summary>
public sealed record UnreadCountDto(int UnreadCount);

/// <summary>Read/set the current user's email-notifications toggle (§6.8).</summary>
public sealed record NotificationSettingsDto(bool EmailNotificationsEnabled);

/// <summary>Body of PUT /api/me/notification-settings.</summary>
public sealed record UpdateNotificationSettingsRequest(bool? EmailNotificationsEnabled);
