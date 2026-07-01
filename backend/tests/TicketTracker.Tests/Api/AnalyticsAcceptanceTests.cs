using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Domain.Entities;
using TicketTracker.Domain.Enums;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite for the analytics dashboard (Wave 3, ADR-0020, §5.4; test-guidance §11 C). Presses
/// edges beyond the developer smoke tests: the median cycle time over an EVEN sample (average of the two
/// middle values); throughput spread across MULTIPLE ISO weeks; the cycle-time modified_at FALLBACK for a
/// done ticket with no ticket_moved activity (a pre-Wave-2 row seeded directly); by-label never counting
/// another team's labels; and analytics being SESSION-ONLY (an API key is off the v1 surface → not reachable).
/// </summary>
public sealed class AnalyticsAcceptanceTests : IntegrationTestBase
{
    private sealed record Ctx(HttpClient Client, Guid UserId, Guid TeamId);

    private async Task<Ctx> SetupTeamAsync(string name = "Platform")
    {
        var (token, userId, _) = await RegisterAdminAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name }));
        return new Ctx(client, userId, team.Id);
    }

    private async Task<Guid> CreateTicketAsync(HttpClient client, Guid teamId)
        => (await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId, type = "bug", title = "Ticket", body = "Body", priority = "medium" }))).Id;

    private Task MoveToDoneAsync(HttpClient client, Guid teamId, Guid ticketId)
        => client.PutAsJsonAsync($"/api/tickets/{ticketId}",
            new { teamId, type = "bug", title = "Ticket", body = "Body", state = "done", priority = "medium" })
            .ContinueWith(t => t.Result.EnsureSuccessStatusCode());

    private Task<DashboardDto> DashboardAsync(HttpClient client, Guid teamId, string? query = null)
        => ReadAsync<DashboardDto>(client.GetAsync(
            $"/api/analytics/dashboard?teamId={teamId}{(query is null ? "" : "&" + query)}").Result);

    // ================= Median over an EVEN sample = average of the two middle values =================

    [Fact]
    public async Task Cycle_time_median_over_an_even_sample_is_the_average_of_the_two_middle_values()
    {
        var ctx = await SetupTeamAsync();

        // Four done tickets with cycle times 2, 4, 6, 8 days. Sorted median = (4+6)/2 = 5; avg = 5.
        var durations = new[] { 2, 4, 6, 8 };
        var start = new DateTime(2026, 06, 01, 12, 0, 0, DateTimeKind.Utc);
        foreach (var days in durations)
        {
            Factory.Clock.SetUtcNow(start);
            var t = await CreateTicketAsync(ctx.Client, ctx.TeamId);
            Factory.Clock.SetUtcNow(start.AddDays(days));
            await MoveToDoneAsync(ctx.Client, ctx.TeamId, t);
        }
        // Bring the clock back to a point that still contains all done days in the default range.
        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 12, 0, 0, DateTimeKind.Utc));

        var dash = await DashboardAsync(ctx.Client, ctx.TeamId, "from=2026-05-01&to=2026-06-30");
        dash.CycleTime.SampleSize.Should().Be(4);
        dash.CycleTime.AvgDays.Should().BeApproximately(5.0, 0.001);
        dash.CycleTime.MedianDays.Should().BeApproximately(5.0, 0.001, "even median = mean of the two middle samples");
    }

    // ================= Throughput spreads across multiple ISO weeks =================

    [Fact]
    public async Task Throughput_buckets_done_tickets_into_their_respective_iso_weeks()
    {
        var ctx = await SetupTeamAsync();

        // Two tickets done in the week of Mon 2026-06-08, one in the week of Mon 2026-06-15.
        async Task DoneOn(DateTime when)
        {
            Factory.Clock.SetUtcNow(when.AddDays(-1));
            var t = await CreateTicketAsync(ctx.Client, ctx.TeamId);
            Factory.Clock.SetUtcNow(when);
            await MoveToDoneAsync(ctx.Client, ctx.TeamId, t);
        }
        await DoneOn(new DateTime(2026, 06, 09, 12, 0, 0, DateTimeKind.Utc)); // Tue, week of 06-08
        await DoneOn(new DateTime(2026, 06, 10, 12, 0, 0, DateTimeKind.Utc)); // Wed, week of 06-08
        await DoneOn(new DateTime(2026, 06, 16, 12, 0, 0, DateTimeKind.Utc)); // Tue, week of 06-15

        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 12, 0, 0, DateTimeKind.Utc));
        var dash = await DashboardAsync(ctx.Client, ctx.TeamId, "from=2026-06-01&to=2026-06-30");

        dash.Throughput.Sum(b => b.DoneCount).Should().Be(3);
        dash.Throughput.Single(b => b.WeekStart == new DateOnly(2026, 06, 08)).DoneCount.Should().Be(2);
        dash.Throughput.Single(b => b.WeekStart == new DateOnly(2026, 06, 15)).DoneCount.Should().Be(1);
        // Buckets are ordered ascending by week start.
        dash.Throughput.Select(b => b.WeekStart).Should().BeInAscendingOrder();
    }

    // ================= Cycle-time fallback to modified_at for a done ticket with no move activity =================

    [Fact]
    public async Task Cycle_time_falls_back_to_modified_at_when_no_ticket_moved_activity_exists()
    {
        var ctx = await SetupTeamAsync();

        // Seed a "pre-Wave-2" done ticket directly: created 06-10, modified (=reached done) 06-13, NO activity.
        var ticketId = Guid.NewGuid();
        await Factory.WithDbAsync(async db =>
        {
            db.Tickets.Add(new Ticket
            {
                Id = ticketId,
                TeamId = ctx.TeamId,
                Type = TicketType.Bug,
                State = TicketState.Done,
                Priority = TicketPriority.Medium,
                Title = "Legacy done",
                Body = "seeded",
                CreatedBy = ctx.UserId,
                CreatedAt = new DateTime(2026, 06, 10, 12, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2026, 06, 13, 12, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        });

        await Factory.WithDbAsync(async db =>
            (await db.ActivityEntries.CountAsync(a => a.TicketId == ticketId)).Should().Be(0,
                "the seeded row has no ticket_moved activity, exercising the modified_at fallback"));

        var dash = await DashboardAsync(ctx.Client, ctx.TeamId, "from=2026-06-01&to=2026-06-30");
        dash.CycleTime.SampleSize.Should().Be(1);
        dash.CycleTime.AvgDays.Should().BeApproximately(3.0, 0.001, "modified_at (06-13) − created_at (06-10) = 3 days");
        dash.Throughput.Single(b => b.WeekStart == new DateOnly(2026, 06, 08)).DoneCount.Should().Be(1);
    }

    // ================= by-label never counts another team's labels =================

    [Fact]
    public async Task By_label_only_includes_the_requested_teams_labels()
    {
        var ctx = await SetupTeamAsync("Team A");
        var labelA = await ReadAsync<LabelDto>(await ctx.Client.PostAsJsonAsync("/api/labels",
            new { teamId = ctx.TeamId, name = "Backend", color = "#3b82f6" }));
        var tA = await CreateTicketAsync(ctx.Client, ctx.TeamId);
        (await ctx.Client.PutAsJsonAsync($"/api/tickets/{tA}/labels", new { labelIds = new[] { labelA.Id } }))
            .EnsureSuccessStatusCode();

        // Team B (same admin) has its own label of the same name.
        var teamB = await ReadAsync<TeamDto>(await ctx.Client.PostAsJsonAsync("/api/teams", new { name = "Team B" }));
        var labelB = await ReadAsync<LabelDto>(await ctx.Client.PostAsJsonAsync("/api/labels",
            new { teamId = teamB.Id, name = "Backend", color = "#ef4444" }));
        var tB = await CreateTicketAsync(ctx.Client, teamB.Id);
        (await ctx.Client.PutAsJsonAsync($"/api/tickets/{tB}/labels", new { labelIds = new[] { labelB.Id } }))
            .EnsureSuccessStatusCode();

        var dashA = await DashboardAsync(ctx.Client, ctx.TeamId);
        dashA.ByLabel.Should().ContainSingle();
        dashA.ByLabel.Single().LabelId.Should().Be(labelA.Id, "Team A's dashboard shows only Team A's label");
        dashA.ByLabel.Should().NotContain(l => l.LabelId == labelB.Id, "Team B's label never leaks into Team A's metrics");
    }

    // ================= Analytics is session-only (not an API-key surface) =================

    [Fact]
    public async Task Analytics_is_not_reachable_by_an_api_key()
    {
        var ctx = await SetupTeamAsync();
        var created = await ReadAsync<CreateApiKeyResponseDto>(await ctx.Client.PostAsJsonAsync(
            "/api/me/api-keys", new { name = "CI", scopes = new[] { "tickets:read" } }));

        var key = Factory.CreateClient();
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Secret);

        // Analytics lives off /api/v1, so a ptk_ key is rejected outright (401) — it is a UI/session concern (§5.4).
        (await key.GetAsync($"/api/analytics/dashboard?teamId={ctx.TeamId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================= Partial range (only 'to' supplied) fills 'from' from the default window =================

    [Fact]
    public async Task Only_to_supplied_fills_from_with_the_default_12_week_window()
    {
        var ctx = await SetupTeamAsync();
        var dash = await DashboardAsync(ctx.Client, ctx.TeamId, "to=2026-06-30");
        dash.To.Should().Be(new DateOnly(2026, 06, 30));
        dash.From.Should().Be(new DateOnly(2026, 06, 30).AddDays(-12 * 7), "from defaults to 12 weeks before 'to'");
    }
}
