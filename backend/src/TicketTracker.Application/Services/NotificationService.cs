using Microsoft.EntityFrameworkCore;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;

namespace TicketTracker.Application.Services;

/// <summary>
/// In-app notifications for the current user (Wave 2, §5.3, ADR-0013). Self-scoped BY CONSTRUCTION —
/// every query is filtered by <c>recipient_id == currentUserId</c>, so no other user's notification is
/// ever addressable. Marking another user's notification id read is a 404 (self-owned 404-masking, §5.3).
/// List is newest-first with keyset pagination; <c>unreadCount</c> is returned alongside so the bell can
/// update from the same call.
/// </summary>
public sealed class NotificationService
{
    /// <summary>Clamp bounds for the page size (§5.3).</summary>
    public const int MinLimit = 1;
    public const int MaxLimit = 50;
    public const int DefaultLimit = 20;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public NotificationService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<NotificationListDto> ListAsync(int? limit, string? cursor, CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();
        var take = Math.Clamp(limit ?? DefaultLimit, MinLimit, MaxLimit);

        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == userId);

        // Keyset: everything strictly "after" the cursor position in (created_at DESC, id DESC) order.
        var decoded = KeysetCursor.Decode(cursor);
        if (decoded is { } c)
        {
            query = query.Where(n =>
                n.CreatedAt < c.CreatedAt ||
                (n.CreatedAt == c.CreatedAt && n.Id.CompareTo(c.Id) < 0));
        }

        // Fetch one extra to determine hasMore without a second count.
        var rows = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(take + 1)
            .Select(n => new
            {
                n.Id,
                n.EventType,
                n.Summary,
                n.TicketId,
                n.CommentId,
                n.ActorId,
                ActorName = n.Actor != null ? n.Actor.Name : null,
                ActorEmail = n.Actor != null ? n.Actor.Email : string.Empty,
                n.CreatedAt,
                n.ReadAt
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var page = hasMore ? rows.Take(take).ToList() : rows;

        var items = page
            .Select(n => new NotificationDto(
                n.Id, n.EventType, n.Summary, n.TicketId, n.CommentId, n.ActorId,
                DisplayName(n.ActorName, n.ActorEmail), n.CreatedAt, n.ReadAt))
            .ToList();

        string? nextCursor = hasMore && page.Count > 0
            ? KeysetCursor.Encode(page[^1].CreatedAt, page[^1].Id)
            : null;

        var unreadCount = await CountUnreadAsync(userId, ct);
        return new NotificationListDto(items, unreadCount, hasMore, nextCursor);
    }

    public async Task<UnreadCountDto> UnreadCountAsync(CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();
        return new UnreadCountDto(await CountUnreadAsync(userId, ct));
    }

    /// <summary>
    /// Mark one notification read (idempotent). Resolves by id AND recipient in one query; a row that
    /// exists but belongs to another user is a 404 (self-owned 404-masking, §5.3). Returns the new
    /// unread count so the bell decrements without a second round-trip.
    /// </summary>
    public async Task<UnreadCountDto> MarkReadAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == userId, ct)
            ?? throw ServiceException.NotFound("Notification not found.");

        if (notification.ReadAt is null)
        {
            notification.ReadAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return new UnreadCountDto(await CountUnreadAsync(userId, ct));
    }

    /// <summary>Mark all of the caller's unread notifications read in one pass. Returns unread count = 0.</summary>
    public async Task<UnreadCountDto> MarkAllReadAsync(CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var unread = await _db.Notifications
            .Where(n => n.RecipientId == userId && n.ReadAt == null)
            .ToListAsync(ct);
        if (unread.Count > 0)
        {
            foreach (var n in unread)
                n.ReadAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return new UnreadCountDto(0);
    }

    /// <summary>Read the current user's email-notifications toggle (§6.8, Self).</summary>
    public async Task<NotificationSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var userId = _currentUser.RequireUserId();
        var enabled = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.EmailNotificationsEnabled)
            .FirstOrDefaultAsync(ct);
        return new NotificationSettingsDto(enabled);
    }

    /// <summary>Set the current user's email-notifications toggle (§6.8, Self). Missing flag → 400.</summary>
    public async Task<NotificationSettingsDto> UpdateSettingsAsync(UpdateNotificationSettingsRequest request, CancellationToken ct)
    {
        if (request?.EmailNotificationsEnabled is null)
            throw ServiceException.Validation("emailNotificationsEnabled", "emailNotificationsEnabled is required.");

        var userId = _currentUser.RequireUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ServiceException.NotFound("User not found.");

        var desired = request.EmailNotificationsEnabled.Value;
        if (user.EmailNotificationsEnabled != desired)
        {
            user.EmailNotificationsEnabled = desired;
            await _db.SaveChangesAsync(ct);
        }

        return new NotificationSettingsDto(user.EmailNotificationsEnabled);
    }

    private Task<int> CountUnreadAsync(Guid userId, CancellationToken ct)
        => _db.Notifications.CountAsync(n => n.RecipientId == userId && n.ReadAt == null, ct);

    private static string DisplayName(string? name, string email)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? email : trimmed;
    }
}
