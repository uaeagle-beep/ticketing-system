using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;
using TicketTracker.Domain.Entities;
using TicketTracker.Tests.Fakes;

namespace TicketTracker.Tests.Unit;

/// <summary>
/// Service-level business-flow test (ARCHITECTURE §10 required test #1) exercising
/// <see cref="TicketService"/> directly over real SQLite: modified_at advances on an actual
/// field/state change (V19); a normalized-identical save is a no-op (V20); adding a comment via
/// <see cref="CommentService"/> never touches ticket.modified_at (V21); a cross-team epic is
/// rejected (V16). These run without the web host for a fast, focused signal.
/// </summary>
public sealed class TicketServiceModifiedAtTests : IDisposable
{
    private readonly SqliteTestContext _ctx = new();
    private readonly TestClock _clock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private TicketService NewTicketService()
        => new(_ctx.Db, _clock, new FakeCurrentUser(_userId));

    private CommentService NewCommentService()
        => new(_ctx.Db, _clock, new FakeCurrentUser(_userId));

    public TicketServiceModifiedAtTests()
    {
        // Seed a user, team and epic directly (these have no service-level create dependency here).
        _ctx.Db.Users.Add(new User
        {
            Id = _userId,
            Email = "author@dataart.com",
            EmailNormalized = "author@dataart.com",
            PasswordHash = "x",
            EmailVerified = true,
            CreatedAt = _clock.UtcNow
        });
        _ctx.Db.SaveChanges();
    }

    public void Dispose() => _ctx.Dispose();

    private Guid SeedTeam(string name)
    {
        var id = Guid.NewGuid();
        _ctx.Db.Teams.Add(new Team
        {
            Id = id,
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
            CreatedAt = _clock.UtcNow,
            ModifiedAt = _clock.UtcNow
        });
        _ctx.Db.SaveChanges();
        return id;
    }

    private Guid SeedEpic(Guid teamId, string title)
    {
        var id = Guid.NewGuid();
        _ctx.Db.Epics.Add(new Epic
        {
            Id = id,
            TeamId = teamId,
            Title = title,
            CreatedAt = _clock.UtcNow,
            ModifiedAt = _clock.UtcNow
        });
        _ctx.Db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task Editing_a_field_advances_modified_at()
    {
        var teamId = SeedTeam("Platform");
        var svc = NewTicketService();

        var created = await svc.CreateAsync(
            new CreateTicketRequest(teamId, "bug", "Login fails", "Steps...", null, null), default);

        _clock.Advance(TimeSpan.FromMinutes(30));
        var updated = await svc.UpdateAsync(created.Id,
            new UpdateTicketRequest(teamId, "feature", null, "Login fails on Safari", "Steps...", "in_progress", "medium"), default);

        updated.ModifiedAt.Should().BeAfter(created.ModifiedAt);
        updated.Type.Should().Be("feature");
        updated.State.Should().Be("in_progress");
    }

    [Fact]
    public async Task Saving_normalized_identical_values_is_a_noop()
    {
        var teamId = SeedTeam("Platform");
        var svc = NewTicketService();

        var created = await svc.CreateAsync(
            new CreateTicketRequest(teamId, "bug", "Login fails", "Steps...", null, "new"), default);

        _clock.Advance(TimeSpan.FromMinutes(30));
        // Same values, but title/body padded with whitespace (normalizes equal) — must not advance.
        // Priority is required in the edit body; re-sent at its default so the update is a true no-op.
        var result = await svc.UpdateAsync(created.Id,
            new UpdateTicketRequest(teamId, "bug", null, "  Login fails  ", " Steps... ", "new", "medium"), default);

        result.ModifiedAt.Should().Be(created.ModifiedAt, "a normalized no-op must not advance modified_at (V20)");
    }

    [Fact]
    public async Task Adding_a_comment_does_not_change_ticket_modified_at()
    {
        var teamId = SeedTeam("Platform");
        var ticketSvc = NewTicketService();
        var commentSvc = NewCommentService();

        var created = await ticketSvc.CreateAsync(
            new CreateTicketRequest(teamId, "bug", "T", "B", null, null), default);

        _clock.Advance(TimeSpan.FromHours(2));
        await commentSvc.AddAsync(created.Id, new CreateCommentRequest("a note"), default);

        // Read the ticket back from a fresh context to verify the persisted value, not a cached one.
        await using var fresh = _ctx.NewContext();
        var persisted = await fresh.Tickets.FindAsync(created.Id);
        persisted!.ModifiedAt.Should().Be(created.ModifiedAt, "adding a comment must not touch ticket.modified_at (V21)");
    }

    [Fact]
    public async Task Cross_team_epic_is_rejected_with_epic_team_mismatch()
    {
        var platform = SeedTeam("Platform");
        var payments = SeedTeam("Payments");
        var paymentsEpic = SeedEpic(payments, "Pay Epic");
        var svc = NewTicketService();

        var act = async () => await svc.CreateAsync(
            new CreateTicketRequest(platform, "bug", "T", "B", paymentsEpic, null), default);

        var ex = await act.Should().ThrowAsync<ServiceException>();
        ex.Which.Code.Should().Be(ServiceErrorCode.EpicTeamMismatch);
    }
}
