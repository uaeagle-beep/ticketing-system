using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Real-time push smoke tests (Wave 3, ADR-0019, §11 B). The <c>RealtimeNotifier</c> handler + the
/// <c>NotificationFanout</c> bell ping run over the SAME after-commit event backbone as activity /
/// notifications, so a real HTTP mutation drives them; the production SignalRRealtimeNotifier is replaced by
/// the recording fake (<see cref="CustomWebApplicationFactory.Realtime"/>) so we assert "a board-changed
/// signal for team X was pushed to the right group" and "the watchers got a bell ping" with NO live
/// WebSocket / IHubContext. Push correctness lives in the seam; the transport is a thin shell tested
/// separately in <see cref="BoardHubTests"/>. (Full behaviour coverage is the Tester's job.)
/// </summary>
public sealed class RealtimeNotifierTests : IntegrationTestBase
{
    private async Task<(HttpClient Admin, Guid AdminId, Guid TeamId, Guid TicketId)> SetupAsync()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return (admin, adminId, team.Id, ticket.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> AddMemberAsync(Guid teamId)
    {
        var (token, userId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(userId, teamId);
        return (userId, Authed(token));
    }

    // ---- A move pushes a board signal + a ticket signal to the right groups ----

    [Fact]
    public async Task Ticket_move_pushes_board_and_ticket_signals_for_the_right_team()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        Factory.Realtime.Reset(); // ignore signals from the create-ticket setup above

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        Factory.Realtime.BoardChanges.Should().Contain(teamId,
            "a ticket move signals the team's board group so subscribed boards refetch");
        Factory.Realtime.TicketChanges.Should().Contain(c => c.TicketId == ticketId && c.TeamId == teamId,
            "a ticket move signals the ticket group with the ticket + its team");
    }

    // ---- A comment (notifiable) pushes a ticket signal AND a per-watcher bell ping ----

    [Fact]
    public async Task Comment_add_pushes_ticket_signal_and_a_bell_ping_to_each_watcher_not_the_actor()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        var (watcherId, watcher) = await AddMemberAsync(teamId);
        (await watcher.PostAsync($"/api/tickets/{ticketId}/watch", null)).EnsureSuccessStatusCode();
        Factory.Realtime.Reset(); // ignore setup signals (watch does not notify, but be explicit)

        // The admin comments; the watcher (not the actor) gets a notification row → a bell ping.
        (await admin.PostAsJsonAsync($"/api/tickets/{ticketId}/comments", new { body = "note" }))
            .EnsureSuccessStatusCode();

        Factory.Realtime.TicketChanges.Should().Contain(c => c.TicketId == ticketId && c.TeamId == teamId,
            "a comment changes the ticket's detail (comments list) so the ticket group is signalled");
        Factory.Realtime.BoardChanges.Should().Contain(teamId,
            "a comment is an after-commit ticket event so the board is signalled too");
        Factory.Realtime.UserNotifies.Should().Contain(watcherId,
            "a watcher who received a notification row gets a live bell ping (ADR-0019, §6.4)");
    }

    [Fact]
    public async Task Bell_ping_is_never_sent_to_the_actor_of_their_own_action()
    {
        var (admin, adminId, teamId, ticketId) = await SetupAsync();
        // The admin auto-watches the ticket they created, but is the actor of every action below.
        var (_, watcher) = await AddMemberAsync(teamId);
        Factory.Realtime.Reset();

        // The admin (actor) moves the ticket. The admin is a watcher of their own ticket but is excluded from
        // fan-out, so no notification row → no bell ping to the admin. (A move is not notifiable to the actor.)
        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        Factory.Realtime.UserNotifies.Should().NotContain(adminId,
            "the actor is never notified about their own action, so they get no bell ping");
    }

    // ---- No subscription/team needed for real-time: signals fire regardless of watchers ----

    [Fact]
    public async Task Board_signal_fires_even_with_no_watchers_so_open_boards_still_refetch()
    {
        var (admin, _, teamId, ticketId) = await SetupAsync();
        Factory.Realtime.Reset();

        (await admin.PatchAsJsonAsync($"/api/tickets/{ticketId}/state", new { state = "in_progress" }))
            .EnsureSuccessStatusCode();

        Factory.Realtime.BoardChanges.Should().ContainSingle().Which.Should().Be(teamId,
            "the board signal is independent of watchers — any open board of the team must refetch");
    }
}
