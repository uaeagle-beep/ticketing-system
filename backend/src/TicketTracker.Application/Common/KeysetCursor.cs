using System.Text;

namespace TicketTracker.Application.Common;

/// <summary>
/// Opaque keyset-pagination cursor encoding the last item's <c>(created_at, id)</c> (WAVE2_DESIGN §5.3):
/// base64 of <c>{createdAtTicks}|{id}</c>. Cheaper and more stable than OFFSET under new inserts. A
/// malformed cursor is treated as "start from the beginning" (returns null) rather than an error, so a
/// stale client link degrades gracefully. Ordering is <c>created_at DESC, id DESC</c>.
/// </summary>
public static class KeysetCursor
{
    public static string Encode(DateTime createdAt, Guid id)
    {
        var raw = $"{createdAt.Ticks}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>Decodes the cursor, or returns null if it is null/empty/malformed (start from beginning).</summary>
    public static (DateTime CreatedAt, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2)
                return null;
            if (!long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var id))
                return null;
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (FormatException)
        {
            return null; // malformed base64 → start from the beginning
        }
    }
}
