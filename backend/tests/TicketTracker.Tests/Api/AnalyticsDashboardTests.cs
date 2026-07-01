using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Developer smoke tests for the analytics dashboard (Wave 3, ADR-0020, API_CONTRACT §5.4). Samples the
/// load-bearing behaviours: correct aggregates for a seeded team (by state/priority/type/label, open-vs-done,
/// overdue, WIP-vs-limit), throughput bucketed by ISO week + cycle time derived from `ticket_moved` activity,
/// the date-range filter, an empty team returning an all-zero DTO (no crash), and team-scoped anti-IDOR
/// (unknown team → 404, non-member → 403, admin any team). All aggregation runs on the in-memory SQLite
/// harness (ADR-0002) so this also proves SQLite portability. Full acceptance coverage is the Tester's job.
/// </summary>
public sealed class AnalyticsDashboardTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid UserId, Guid TeamId);

    private async Task<Ctx> SetupTeamAsync(string name = "Platform")
    {
        var (token, userId, _) = await RegisterAdminAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name }));
        return new Ctx(client, userId, team.Id);
    }

    private async Task<Guid> CreateTicketAsync(
        HttpClient client, Guid teamId, string type = "bug", string priority = "medium",
        string title = "Ticket", string? dueDate = null)
    {
        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId, type, title, body = "Body", priority, dueDate }));
        return ticket.Id;
    }

    private async Task MoveAsync(HttpClient client, Guid teamId, Guid ticketId, string type, string priority, string state)
        => (await client.PutAsJsonAsync($"/api/tickets/{ticketId}", new
        {
            teamId, type, title = "Ticket", body = "Body", state, priority
        })).EnsureSuccessStatusCode();

    private Task<HttpResponseMessage> GetDashboardResponseAsync(HttpClient client, Guid teamId, string? query = null)
        => client.GetAsync($"/api/analytics/dashboard?teamId={teamId}{(query is null ? "" : "&" + query)}");

    // ---- Aggregates for a seeded team ----

    [Fact]
    public async Task Dashboard_aggregates_by_state_priority_type_and_open_vs_done()
    {
        var ctx = await SetupTeamAsync();

        // Three tickets: two bugs (medium), one feature (high). Move one bug to done.
        var t1 = await CreateTicketAsync(ctx.Client, ctx.TeamId, type: "bug", priority: "medium");
        await CreateTicketAsync(ctx.Client, ctx.TeamId, type: "bug", priority: "medium");
        await CreateTicketAsync(ctx.Client, ctx.TeamId, type: "feature", priority: "high");
        await MoveAsync(ctx.Client, ctx.TeamId, t1, "bug", "medium", "done");

        var dash = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));

        // All five states present; one done, two new.
        dash.ByState.Should().ContainKeys("new", "ready_for_implementation", "in_progress", "ready_for_acceptance", "done");
        dash.ByState["done"].Should().Be(1);
        dash.ByState["new"].Should().Be(2);

        // Priority + type maps carry every canonical key with correct counts.
        dash.ByPriority["medium"].Should().Be(2);
        dash.ByPriority["high"].Should().Be(1);
        dash.ByPriority["low"].Should().Be(0);
        dash.ByType["bug"].Should().Be(2);
        dash.ByType["feature"].Should().Be(1);
        dash.ByType["fix"].Should().Be(0);

        // Open (non-done) vs done.
        dash.OpenVsDone.Open.Should().Be(2);
        dash.OpenVsDone.Done.Should().Be(1);
    }

    [Fact]
    public async Task Dashboard_by_label_counts_and_carries_name_and_color()
    {
        var ctx = await SetupTeamAsync();
        var label = await ReadAsync<LabelDto>(await ctx.Client.PostAsJsonAsync("/api/labels",
            new { teamId = ctx.TeamId, name = "Backend", color = "#3b82f6" }));

        var t1 = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        var t2 = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        await CreateTicketAsync(ctx.Client, ctx.TeamId); // unlabeled — not counted

        foreach (var t in new[] { t1, t2 })
            (await ctx.Client.PutAsJsonAsync($"/api/tickets/{t}/labels", new { labelIds = new[] { label.Id } }))
                .EnsureSuccessStatusCode();

        var dash = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));

        dash.ByLabel.Should().ContainSingle(l => l.LabelId == label.Id);
        var row = dash.ByLabel.Single(l => l.LabelId == label.Id);
        row.Count.Should().Be(2);
        row.Name.Should().Be("Backend");
        row.Color.Should().Be("#3b82f6");
    }

    [Fact]
    public async Task Dashboard_overdue_counts_past_due_not_done()
    {
        var ctx = await SetupTeamAsync();
        var today = DateOnly.FromDateTime(Factory.Clock.UtcNow);
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd");
        var tomorrow = today.AddDays(1).ToString("yyyy-MM-dd");

        await CreateTicketAsync(ctx.Client, ctx.TeamId, dueDate: yesterday);   // overdue
        await CreateTicketAsync(ctx.Client, ctx.TeamId, dueDate: tomorrow);    // not overdue
        var doneOverdue = await CreateTicketAsync(ctx.Client, ctx.TeamId, dueDate: yesterday);
        await MoveAsync(ctx.Client, ctx.TeamId, doneOverdue, "bug", "medium", "done"); // done → not overdue

        var dash = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));
        dash.OverdueCount.Should().Be(1);
    }

    [Fact]
    public async Task Dashboard_wip_reports_count_limit_and_over_limit_per_state()
    {
        var ctx = await SetupTeamAsync();

        // Cap in_progress at 1, then push two tickets into it (WIP enforcement blocks the 2nd move via the
        // board rules, so we set the limit AFTER moving to create an over-limit condition for the metric).
        var a = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        var b = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        await MoveAsync(ctx.Client, ctx.TeamId, a, "bug", "medium", "in_progress");
        await MoveAsync(ctx.Client, ctx.TeamId, b, "bug", "medium", "in_progress");
        (await ctx.Client.PutAsJsonAsync($"/api/teams/{ctx.TeamId}/wip-limits",
            new { wipLimits = new Dictionary<string, int?> { ["in_progress"] = 1 } })).EnsureSuccessStatusCode();

        var dash = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));

        dash.Wip.Should().HaveCount(5); // all five states in workflow order
        var inProgress = dash.Wip.Single(w => w.State == "in_progress");
        inProgress.Count.Should().Be(2);
        inProgress.Limit.Should().Be(1);
        inProgress.OverLimit.Should().BeTrue();

        var newState = dash.Wip.Single(w => w.State == "new");
        newState.Limit.Should().BeNull("no cap configured for new = unlimited");
        newState.OverLimit.Should().BeFalse();
    }

    // ---- Throughput + cycle time from activity ----

    [Fact]
    public async Task Dashboard_throughput_and_cycle_time_derive_from_ticket_moved_activity()
    {
        var ctx = await SetupTeamAsync();

        // Create a ticket "5 days ago", then advance the clock and move it to done "today" → cycle time ≈ 5d.
        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 20, 12, 0, 0, DateTimeKind.Utc));
        var t = await CreateTicketAsync(ctx.Client, ctx.TeamId);

        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 25, 12, 0, 0, DateTimeKind.Utc));
        await MoveAsync(ctx.Client, ctx.TeamId, t, "bug", "medium", "done");

        var dash = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));

        // Throughput: exactly one done ticket, bucketed into the ISO week starting Monday 2026-06-22.
        dash.Throughput.Sum(b => b.DoneCount).Should().Be(1);
        var bucket = dash.Throughput.Single();
        bucket.WeekStart.Should().Be(new DateOnly(2026, 06, 22)); // Monday of the week containing Thu 2026-06-25
        bucket.DoneCount.Should().Be(1);

        // Cycle time: one sample, ~5 days (created 06-20 12:00 → done 06-25 12:00).
        dash.CycleTime.SampleSize.Should().Be(1);
        dash.CycleTime.AvgDays.Should().BeApproximately(5.0, 0.001);
        dash.CycleTime.MedianDays.Should().BeApproximately(5.0, 0.001);
    }

    [Fact]
    public async Task Dashboard_date_range_filters_throughput_to_the_range()
    {
        var ctx = await SetupTeamAsync();

        // A ticket done "today" (2026-06-30) and one done a month earlier (2026-05-25).
        Factory.Clock.SetUtcNow(new DateTime(2026, 05, 25, 10, 0, 0, DateTimeKind.Utc));
        var old = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        await MoveAsync(ctx.Client, ctx.TeamId, old, "bug", "medium", "done");

        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 10, 0, 0, DateTimeKind.Utc));
        var recent = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        await MoveAsync(ctx.Client, ctx.TeamId, recent, "bug", "medium", "done");

        // Range covering only June → only the recent ticket counts toward throughput.
        var dash = await ReadAsync<DashboardDto>(
            await GetDashboardResponseAsync(ctx.Client, ctx.TeamId, "from=2026-06-01&to=2026-06-30"));

        dash.From.Should().Be(new DateOnly(2026, 06, 01));
        dash.To.Should().Be(new DateOnly(2026, 06, 30));
        dash.Throughput.Sum(b => b.DoneCount).Should().Be(1, "only the June done-ticket falls in range");
        dash.CycleTime.SampleSize.Should().Be(1);
    }

    [Fact]
    public async Task Dashboard_from_after_to_is_400()
    {
        var ctx = await SetupTeamAsync();
        var resp = await GetDashboardResponseAsync(ctx.Client, ctx.TeamId, "from=2026-07-01&to=2026-06-01");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("validation_error");
    }

    [Fact]
    public async Task Dashboard_invalid_date_is_400()
    {
        var ctx = await SetupTeamAsync();
        var resp = await GetDashboardResponseAsync(ctx.Client, ctx.TeamId, "from=not-a-date");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("from");
    }

    // ---- Empty team ----

    [Fact]
    public async Task Dashboard_for_empty_team_returns_all_zero_without_crashing()
    {
        var ctx = await SetupTeamAsync();
        var dash = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));

        dash.ByState.Values.Sum().Should().Be(0);
        dash.ByPriority.Values.Sum().Should().Be(0);
        dash.ByType.Values.Sum().Should().Be(0);
        dash.ByLabel.Should().BeEmpty();
        dash.OpenVsDone.Open.Should().Be(0);
        dash.OpenVsDone.Done.Should().Be(0);
        dash.Throughput.Should().BeEmpty();
        dash.CycleTime.SampleSize.Should().Be(0);
        dash.CycleTime.AvgDays.Should().BeNull();
        dash.CycleTime.MedianDays.Should().BeNull();
        dash.OverdueCount.Should().Be(0);
        dash.Wip.Should().HaveCount(5);
        dash.Wip.Should().OnlyContain(w => w.Count == 0 && !w.OverLimit);
    }

    // ---- Team scoping / anti-IDOR ----

    [Fact]
    public async Task Dashboard_for_unknown_team_is_404()
    {
        var ctx = await SetupTeamAsync();
        var resp = await GetDashboardResponseAsync(ctx.Client, Guid.NewGuid());
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboard_missing_teamId_is_400()
    {
        var (token, _, _) = await RegisterAdminAsync();
        var resp = await Authed(token).GetAsync("/api/analytics/dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("teamId");
    }

    [Fact]
    public async Task Non_member_cannot_read_another_teams_dashboard_403()
    {
        var ctx = await SetupTeamAsync();
        await CreateTicketAsync(ctx.Client, ctx.TeamId);

        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var resp = await GetDashboardResponseAsync(Authed(outsiderToken), ctx.TeamId);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_of_the_team_can_read_its_dashboard()
    {
        var ctx = await SetupTeamAsync();
        await CreateTicketAsync(ctx.Client, ctx.TeamId);

        var (memberToken, _, _) = await RegisterMemberInTeamAsync(ctx.TeamId);
        var resp = await GetDashboardResponseAsync(Authed(memberToken), ctx.TeamId);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<DashboardDto>(resp)).ByState.Values.Sum().Should().Be(1);
    }

    [Fact]
    public async Task Admin_can_read_any_teams_dashboard()
    {
        // A team owned/created by one admin; a DIFFERENT admin (not a member) still sees it (admin any-team).
        var ctx = await SetupTeamAsync();
        await CreateTicketAsync(ctx.Client, ctx.TeamId);

        var (otherAdminToken, _, _) = await RegisterAdminAsync();
        var resp = await GetDashboardResponseAsync(Authed(otherAdminToken), ctx.TeamId);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<DashboardDto>(resp)).ByState.Values.Sum().Should().Be(1);
    }

    // ---- Team isolation: a second team's tickets never leak ----

    [Fact]
    public async Task Dashboard_never_leaks_another_teams_tickets()
    {
        var ctx = await SetupTeamAsync("Team A");
        await CreateTicketAsync(ctx.Client, ctx.TeamId); // 1 ticket in Team A

        // Same admin creates a second team with two tickets.
        var teamB = await ReadAsync<TeamDto>(await ctx.Client.PostAsJsonAsync("/api/teams", new { name = "Team B" }));
        await CreateTicketAsync(ctx.Client, teamB.Id);
        await CreateTicketAsync(ctx.Client, teamB.Id);

        var dashA = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, ctx.TeamId));
        dashA.ByState.Values.Sum().Should().Be(1, "Team A's dashboard counts only Team A's tickets");

        var dashB = await ReadAsync<DashboardDto>(await GetDashboardResponseAsync(ctx.Client, teamB.Id));
        dashB.ByState.Values.Sum().Should().Be(2, "Team B's dashboard counts only Team B's tickets");
    }

    // ---- Unauthenticated ----

    [Fact]
    public async Task Dashboard_requires_authentication_401()
    {
        var ctx = await SetupTeamAsync();
        var resp = await Client.GetAsync($"/api/analytics/dashboard?teamId={ctx.TeamId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
