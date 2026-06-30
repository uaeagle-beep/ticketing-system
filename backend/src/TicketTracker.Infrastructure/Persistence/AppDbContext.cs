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
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<WipLimit> WipLimits => Set<WipLimit>();
    public DbSet<UserTeam> UserTeams => Set<UserTeam>();

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
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.EpicId).HasColumnName("epic_id");
            e.Property(x => x.Type).HasConversion(ticketTypeConverter).HasColumnName("type").HasMaxLength(16).IsRequired();
            e.Property(x => x.State).HasConversion(ticketStateConverter).HasColumnName("state").HasMaxLength(32).IsRequired();
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
    }

    private static TicketType ParseType(string value)
        => EnumCanonical.TryParseType(value, out var t)
            ? t
            : throw new InvalidOperationException($"Unknown ticket type in store: '{value}'.");

    private static TicketState ParseState(string value)
        => EnumCanonical.TryParseState(value, out var s)
            ? s
            : throw new InvalidOperationException($"Unknown ticket state in store: '{value}'.");
}
