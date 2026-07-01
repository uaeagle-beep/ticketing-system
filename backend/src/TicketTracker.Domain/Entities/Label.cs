namespace TicketTracker.Domain.Entities;

/// <summary>
/// Team-scoped label/tag (Wave 2, ADR-0016). A label belongs to a single <see cref="Team"/> and its
/// name is unique WITHIN that team, case-insensitively, via the <see cref="NameNormalized"/> companion
/// column + a composite unique index <c>ux_labels_team_name (team_id, name_normalized)</c> — the same
/// pattern as team-name uniqueness (ADR-0002). Two teams may each have a "bug" label; a collision inside
/// one team → <c>409 duplicate_label_name</c>. <c>team_id</c> CASCADE (a label is owned by its team; it is
/// pure metadata and never blocks team deletion — the existing team_has_children guard governs that, §4.1).
/// Labels raise no activity/notification events in Wave 2 (ADR-0016 / W2-LABEL-NOEVENTS).
/// </summary>
public class Label
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>Trimmed display value (non-empty). <c>FieldLimits.LabelNameMax = 50</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>trim(lower(name)) — case-insensitive per-team uniqueness key.</summary>
    public string NameNormalized { get; set; } = string.Empty;

    /// <summary>"#RRGGBB" (7 chars incl. '#'), lowercased; validated by regex in the service.</summary>
    public string Color { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
