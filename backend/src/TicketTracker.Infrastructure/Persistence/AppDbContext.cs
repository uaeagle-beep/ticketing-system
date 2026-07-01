using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TicketTracker.Application.Abstractions;
using TicketTracker.Application.Validation;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;

namespace TicketTracker.Infrastructure.Persistence;

/// <summary>
/// EF Core model + provider configuration (ARCHITECTURE §4). The model is deliberately
/// provider-agnostic so the SAME schema/constraints are exercised under Npgsql (production)
/// and SQLite (tests) — ADR-0002. Enums are stored as canonical lowercase text; case-insensitive
/// uniqueness uses normalized companion columns with plain unique indexes (no citext / PG-only types).
/// </summary>
public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<WipLimit> WipLimits => Set<WipLimit>();
    public DbSet<UserTeam> UserTeams => Set<UserTeam>();
    public DbSet<TicketAssignee> TicketAssignees => Set<TicketAssignee>();
    public DbSet<TicketWatcher> TicketWatchers => Set<TicketWatcher>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TicketLabel> TicketLabels => Set<TicketLabel>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Canonical lowercase text converters for the two enums (ARCHITECTURE §4.2).
        var ticketTypeConverter = new ValueConverter<TicketType, string>(
            v => EnumCanonical.ToCanonical(v),
            v => ParseType(v));
        var ticketStateConverter = new ValueConverter<TicketState, string>(
            v => EnumCanonical.ToCanonical(v),
            v => ParseState(v));
        var ticketPriorityConverter = new ValueConverter<TicketPriority, string>(
            v => EnumCanonical.ToCanonical(v),
            v => ParsePriority(v));

        // ---------- User ----------
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            // Optional display name (nullable). Falls back to email in the UI when null.
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(FieldLimits.NameMax);
            e.Property(x => x.EmailNormalized).HasColumnName("email_normalized").HasMaxLength(320).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.EmailVerified).HasColumnName("email_verified").IsRequired();
            // Authorization flags (ADR-0007). Default false so Npgsql and SQLite agree (ADR-0008).
            e.Property(x => x.IsAdmin).HasColumnName("is_admin").IsRequired().HasDefaultValue(false);
            e.Property(x => x.IsBlocked).HasColumnName("is_blocked").IsRequired().HasDefaultValue(false);
            // Wave 2 (§4.7): global email-notifications toggle. Store default is semantically permanent
            // (unlike Wave-1's priming-only priority default), so keep .HasDefaultValue(true) on the model
            // — this backfills existing rows AND keeps has-pending-model-changes clean.
            e.Property(x => x.EmailNotificationsEnabled)
                .HasColumnName("email_notifications_enabled").IsRequired().HasDefaultValue(true);
            // Wave 3 i18n (§4.6): nullable preferred locale (uk|en). Null = unset → client detection / uk.
            // Stored only in Phase 2; the i18n phase wires it into the profile API + email localization.
            e.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(5);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasIndex(x => x.EmailNormalized).IsUnique(); // case-insensitive uniqueness key (V1)
        });

        // ---------- UserTeam (membership join) ----------
        b.Entity<UserTeam>(e =>
        {
            e.ToTable("user_teams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // A user cannot be in the same team twice (INV-1).
            e.HasIndex(x => new { x.UserId, x.TeamId }).IsUnique().HasDatabaseName("ux_user_teams_user_team");
            e.HasIndex(x => x.TeamId); // "members of team T" lookups
            e.HasOne(x => x.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade); // membership is an association, not authored content (§2.2)
            e.HasOne(x => x.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- EmailVerificationToken ----------
        b.Entity<EmailVerificationToken>(e =>
        {
            e.ToTable("email_verification_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
            e.HasIndex(x => x.TokenHash);
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User)
                .WithMany(u => u.VerificationTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade); // auth artifacts owned by the user
        });

        // ---------- PasswordResetToken ----------
        // Structural twin of EmailVerificationToken in its own table (F-01, ADR-0010): distinct TTL,
        // single-use, no shared "token type" discriminator (avoids cross-flow acceptance).
        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
            e.HasIndex(x => x.TokenHash);
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade); // auth artifacts owned by the user
        });

        // ---------- Session ----------
        b.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Team ----------
        b.Entity<Team>(e =>
        {
            e.ToTable("teams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.NameNormalized).HasColumnName("name_normalized").HasMaxLength(200).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ModifiedAt).HasColumnName("modified_at").IsRequired();
            e.HasIndex(x => x.NameNormalized).IsUnique(); // case-insensitive uniqueness (V8)
        });

        // ---------- WipLimit ----------
        b.Entity<WipLimit>(e =>
        {
            e.ToTable("wip_limits", t =>
            {
                t.HasCheckConstraint("ck_wip_limits_state",
                    "state IN ('new','ready_for_implementation','in_progress','ready_for_acceptance','done')");
                // Value bound mirrors TeamService validation ([1, 999]) as a DB backstop.
                t.HasCheckConstraint("ck_wip_limits_max_count", "max_count >= 1 AND max_count <= 999");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(32).IsRequired();
            e.Property(x => x.MaxCount).HasColumnName("max_count").IsRequired();
            // One limit row per (team, state) — absence means unlimited.
            e.HasIndex(x => new { x.TeamId, x.State }).IsUnique().HasDatabaseName("ux_wip_limits_team_state");
            e.HasOne(x => x.Team)
                .WithMany(t => t.WipLimits)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade); // limits are owned by the team (deleted with it)
        });

        // ---------- Epic ----------
        b.Entity<Epic>(e =>
        {
            e.ToTable("epics");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.Title).HasMaxLength(512).IsRequired();
            e.Property(x => x.Description);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ModifiedAt).HasColumnName("modified_at").IsRequired();
            e.HasIndex(x => x.TeamId);
            e.HasOne(x => x.Team)
                .WithMany(t => t.Epics)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict); // cannot delete a team with epics -> 409 (V9)
        });

        // ---------- Ticket ----------
        b.Entity<Ticket>(e =>
        {
            e.ToTable("tickets", t =>
            {
                t.HasCheckConstraint("ck_tickets_type", "type IN ('bug','feature','fix')");
                t.HasCheckConstraint("ck_tickets_state",
                    "state IN ('new','ready_for_implementation','in_progress','ready_for_acceptance','done')");
                t.HasCheckConstraint("ck_tickets_priority", "priority IN ('low','medium','high','urgent')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.EpicId).HasColumnName("epic_id");
            e.Property(x => x.Type).HasConversion(ticketTypeConverter).HasColumnName("type").HasMaxLength(16).IsRequired();
            e.Property(x => x.State).HasConversion(ticketStateConverter).HasColumnName("state").HasMaxLength(32).IsRequired();
            // Priority stored as canonical lowercase text; CHECK backstop above. The model carries NO
            // store default — the migration's AddColumn defaultValue "medium" backfills existing rows only
            // (keeps has-pending-model-changes clean, WAVE1_DESIGN §3.1). New rows are app-set on create.
            e.Property(x => x.Priority).HasConversion(ticketPriorityConverter).HasColumnName("priority").HasMaxLength(16).IsRequired();
            // Optional calendar-day due date. DateOnly? -> PG 'date', SQLite TEXT 'YYYY-MM-DD' (ADR-0002).
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.Title).HasMaxLength(512).IsRequired();
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ModifiedAt).HasColumnName("modified_at").IsRequired();
            e.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

            // Board query: per-team, grouped by state, ordered by modified desc (A22).
            e.HasIndex(x => new { x.TeamId, x.State, x.ModifiedAt }).HasDatabaseName("ix_tickets_board");
            e.HasIndex(x => x.EpicId);
            e.HasIndex(x => x.CreatedBy);

            e.HasOne(x => x.Team)
                .WithMany(t => t.Tickets)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict); // cannot delete a team with tickets -> 409 (V9)

            e.HasOne(x => x.Epic)
                .WithMany(ep => ep.Tickets)
                .HasForeignKey(x => x.EpicId)
                .OnDelete(DeleteBehavior.Restrict); // cannot delete a referenced epic -> 409 (V12)

            e.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict); // protect authorship integrity
        });

        // ---------- Comment ----------
        b.Entity<Comment>(e =>
        {
            e.ToTable("comments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
            e.Property(x => x.AuthorId).HasColumnName("author_id").IsRequired();
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // F-12 (WAVE2 §4.6): null = never edited; set on a real body change by CommentService.EditAsync.
            e.Property(x => x.EditedAt).HasColumnName("edited_at");
            e.HasIndex(x => new { x.TicketId, x.CreatedAt }).HasDatabaseName("ix_comments_ticket_created");

            e.HasOne(x => x.Ticket)
                .WithMany(t => t.Comments)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade); // ONLY mandated cascade (V22)

            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- TicketAssignee (assignment join) ----------
        b.Entity<TicketAssignee>(e =>
        {
            e.ToTable("ticket_assignees");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // No user assigned to the same ticket twice (INV-W1).
            e.HasIndex(x => new { x.TicketId, x.UserId }).IsUnique().HasDatabaseName("ux_ticket_assignees_ticket_user");
            e.HasIndex(x => x.UserId); // "tickets assigned to user U" / "assigned to me" filter
            e.HasOne(x => x.Ticket)
                .WithMany(t => t.Assignees)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade); // assignment is not standalone content (mirrors Ticket→Comment)
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict); // protect user integrity (mirrors created_by/author_id)
        });

        // Single source of truth for the event_type CHECK on notifications + activity_entries (§6.1).
        var eventTypeCheck = $"event_type IN ({EventTypeCanonical.CheckConstraintValues()})";

        // ---------- TicketWatcher (subscription join — like TicketAssignee, but BOTH FKs CASCADE) ----------
        b.Entity<TicketWatcher>(e =>
        {
            e.ToTable("ticket_watchers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // No double-watch (INV): unique (ticket_id, user_id).
            e.HasIndex(x => new { x.TicketId, x.UserId }).IsUnique().HasDatabaseName("ux_ticket_watchers_ticket_user");
            e.HasIndex(x => x.UserId); // "my watched tickets" (possible later)
            e.HasOne(x => x.Ticket)
                .WithMany(t => t.Watchers)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade); // a watch is owned by the ticket
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade); // a watch carries no authorship (mirrors UserTeam)
        });

        // ---------- Notification (in-app + email outbox row) ----------
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications", t =>
            {
                t.HasCheckConstraint("ck_notifications_event_type", eventTypeCheck);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RecipientId).HasColumnName("recipient_id").IsRequired();
            e.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired();
            // NULLABLE ticket_id with ON DELETE SET NULL: a ticket_deleted notification OUTLIVES its
            // ticket (§6.6). THIS IS THE KEY SCHEMA SUBTLETY — NOT CASCADE.
            e.Property(x => x.TicketId).HasColumnName("ticket_id");
            // FK-less by design (§4.3): a comment delete must neither cascade-nuke nor block the row.
            e.Property(x => x.CommentId).HasColumnName("comment_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(500).IsRequired();
            e.Property(x => x.DataJson).HasColumnName("data_json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ReadAt).HasColumnName("read_at");
            e.Property(x => x.EmailedAt).HasColumnName("emailed_at");

            // "my notifications newest-first" and "my unread count".
            e.HasIndex(x => new { x.RecipientId, x.ReadAt, x.CreatedAt })
                .HasDatabaseName("ix_notifications_recipient_unread");
            // The outbox scan: WHERE emailed_at IS NULL AND created_at <= cutoff.
            e.HasIndex(x => new { x.EmailedAt, x.CreatedAt })
                .HasDatabaseName("ix_notifications_outbox");

            e.HasOne(x => x.Recipient)
                .WithMany()
                .HasForeignKey(x => x.RecipientId)
                .OnDelete(DeleteBehavior.Cascade); // a notification is owned by its recipient
            e.HasOne(x => x.Actor)
                .WithMany()
                .HasForeignKey(x => x.ActorId)
                .OnDelete(DeleteBehavior.Restrict); // preserve "who did it" (mirrors created_by/author_id)
            e.HasOne(x => x.Ticket)
                .WithMany()
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.SetNull); // a ticket_deleted notification survives its ticket (§6.6)
            // NO relationship configured for CommentId — intentionally FK-less (§4.3).
        });

        // ---------- ActivityEntry (per-ticket timeline) ----------
        b.Entity<ActivityEntry>(e =>
        {
            e.ToTable("activity_entries", t =>
            {
                t.HasCheckConstraint("ck_activity_entries_event_type", eventTypeCheck);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
            e.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(500).IsRequired();
            e.Property(x => x.DataJson).HasColumnName("data_json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // The ticket-detail timeline lists a ticket's entries chronologically.
            e.HasIndex(x => new { x.TicketId, x.CreatedAt }).HasDatabaseName("ix_activity_ticket_created");
            e.HasOne(x => x.Ticket)
                .WithMany()
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade); // the timeline dies with the ticket
            e.HasOne(x => x.Actor)
                .WithMany()
                .HasForeignKey(x => x.ActorId)
                .OnDelete(DeleteBehavior.Restrict); // preserve audit integrity
        });

        // ---------- Label (team-scoped tag, ADR-0016) ----------
        b.Entity<Label>(e =>
        {
            e.ToTable("labels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.Name).HasMaxLength(FieldLimits.LabelNameMax).IsRequired();
            e.Property(x => x.NameNormalized).HasColumnName("name_normalized").HasMaxLength(FieldLimits.LabelNameMax).IsRequired();
            e.Property(x => x.Color).HasMaxLength(7).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // Case-insensitive uniqueness WITHIN a team (two teams may both have "bug", §4.4).
            e.HasIndex(x => new { x.TeamId, x.NameNormalized }).IsUnique().HasDatabaseName("ux_labels_team_name");
            e.HasOne(x => x.Team)
                .WithMany(t => t.Labels)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade); // a label is owned by its team (pure metadata; never blocks team delete)
        });

        // ---------- TicketLabel (tag join — like TicketAssignee, but BOTH FKs CASCADE) ----------
        b.Entity<TicketLabel>(e =>
        {
            e.ToTable("ticket_labels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
            e.Property(x => x.LabelId).HasColumnName("label_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            // No label tags the same ticket twice.
            e.HasIndex(x => new { x.TicketId, x.LabelId }).IsUnique().HasDatabaseName("ux_ticket_labels_ticket_label");
            e.HasIndex(x => x.LabelId); // board filter "tickets with label L"
            e.HasOne(x => x.Ticket)
                .WithMany(t => t.Labels)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade); // a tag is not standalone content (mirrors Ticket→TicketAssignee)
            e.HasOne(x => x.Label)
                .WithMany()
                .HasForeignKey(x => x.LabelId)
                .OnDelete(DeleteBehavior.Cascade); // removing a label removes it from all tickets
        });

        // ---------- Attachment (file metadata; blob on the volume, ADR-0018) ----------
        // Mirrors the Comment block: ticket CASCADE (owned artifact), uploader RESTRICT (preserve
        // "who uploaded"). The blob itself lives under storage_key on the named volume (§7.1).
        b.Entity<Attachment>(e =>
        {
            e.ToTable("attachments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
            e.Property(x => x.UploadedBy).HasColumnName("uploaded_by").IsRequired();
            e.Property(x => x.OriginalFilename).HasColumnName("original_filename").HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(150).IsRequired();
            e.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            e.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(80).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            // List a ticket's attachments chronologically (§4.2).
            e.HasIndex(x => new { x.TicketId, x.CreatedAt }).HasDatabaseName("ix_attachments_ticket_created");
            // Integrity of the opaque key (the on-disk name is unique).
            e.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_attachments_storage_key");

            e.HasOne(x => x.Ticket)
                .WithMany(t => t.Attachments)
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade); // an attachment is owned by the ticket (mirrors Comment)
            e.HasOne(x => x.Uploader)
                .WithMany()
                .HasForeignKey(x => x.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict); // preserve "who uploaded" (mirrors created_by/author_id)
        });

        // ---------- WebhookSubscription (team-owned outbound integration, ADR-0021 §4.3) ----------
        // Team CASCADE (owned by the team; pure metadata, never blocks team delete), created_by RESTRICT
        // (preserve who wired it). The signing secret is stored AES-GCM-encrypted, never serialized back.
        b.Entity<WebhookSubscription>(e =>
        {
            e.ToTable("webhook_subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
            e.Property(x => x.Url).HasColumnName("url").HasMaxLength(2048).IsRequired();
            e.Property(x => x.SecretEncrypted).HasColumnName("secret_encrypted").IsRequired();
            e.Property(x => x.EventTypes).HasColumnName("event_types").IsRequired();
            e.Property(x => x.Active).HasColumnName("active").IsRequired().HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ModifiedAt).HasColumnName("modified_at").IsRequired();
            e.HasIndex(x => x.TeamId).HasDatabaseName("ix_webhook_subscriptions_team");
            e.HasOne(x => x.Team)
                .WithMany(t => t.WebhookSubscriptions)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade); // a subscription is owned by its team (§4.1)
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict); // preserve who wired the integration (§4.1)
        });

        // ---------- WebhookDelivery (the outbox — mirrors the notification outbox role, ADR-0021 §4.4) ----------
        var webhookStatusCheck = $"status IN ({WebhookDeliveryStatusCanonical.CheckConstraintValues()})";
        // event_type here also permits the synthetic 'webhook_ping' test event (POST .../ping) in addition
        // to the canonical application events.
        var webhookEventTypeCheck =
            $"event_type IN ({EventTypeCanonical.CheckConstraintValues()},'webhook_ping')";
        b.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("webhook_deliveries", t =>
            {
                t.HasCheckConstraint("ck_webhook_deliveries_status", webhookStatusCheck);
                t.HasCheckConstraint("ck_webhook_deliveries_event_type", webhookEventTypeCheck);
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            e.Property(x => x.Attempts).HasColumnName("attempts").IsRequired().HasDefaultValue(0);
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            e.Property(x => x.LastStatusCode).HasColumnName("last_status_code");
            e.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            // The drain scan: WHERE status='pending' AND next_attempt_at <= now (cheap outbox selector).
            e.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("ix_webhook_deliveries_outbox");
            // Per-subscription audit list (newest-first).
            e.HasIndex(x => new { x.SubscriptionId, x.CreatedAt }).HasDatabaseName("ix_webhook_deliveries_subscription");
            e.HasOne(x => x.Subscription)
                .WithMany(s => s.Deliveries)
                .HasForeignKey(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade); // deliveries are owned by the subscription (§4.1)
        });

        // ---------- ApiKey (personal access token, ADR-0021 §4.5) ----------
        // User CASCADE (a key is owned by its user); token hashed at rest + a display prefix; unique hash.
        b.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(12).IsRequired();
            e.Property(x => x.Scopes).HasColumnName("scopes").HasMaxLength(120).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_api_keys_token_hash");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_api_keys_user");
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade); // a key is owned by its user (§4.1)
        });
    }

    private static TicketType ParseType(string value)
        => EnumCanonical.TryParseType(value, out var t)
            ? t
            : throw new InvalidOperationException($"Unknown ticket type in store: '{value}'.");

    private static TicketState ParseState(string value)
        => EnumCanonical.TryParseState(value, out var s)
            ? s
            : throw new InvalidOperationException($"Unknown ticket state in store: '{value}'.");

    private static TicketPriority ParsePriority(string value)
        => EnumCanonical.TryParsePriority(value, out var p)
            ? p
            : throw new InvalidOperationException($"Unknown ticket priority in store: '{value}'.");
}
