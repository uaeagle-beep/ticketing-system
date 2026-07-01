using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Tickets over real HTTP (E4, API_CONTRACT §6). Covers create with all fields + defaults,
/// strict enum validation (type/state → 400), same-team-epic enforcement on create AND update
/// (400 epic_team_mismatch), modified_at semantics (state change advances; no-op save does not),
/// team change with a now-cross-team epic rejected / valid same-team epic accepted, and delete
/// cascading to comments.
/// </summary>
public sealed class TicketsTests : IntegrationTestBase
{

    private sealed record Ctx(HttpClient Client, Guid UserId, Guid TeamId);

    private async Task<Ctx> SetupAsync(string teamName = "Platform")
    {
        var (token, userId, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = teamName }));
        return new Ctx(client, userId, team.Id);
    }

    [Fact]
    public async Task Create_with_all_fields_persists_and_defaults_state_to_new()
    {
        var ctx = await SetupAsync();
        var epic = await ReadAsync<EpicDto>(
            await ctx.Client.PostAsJsonAsync("/api/epics", new { teamId = ctx.TeamId, title = "Billing Revamp" }));

        var created = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "  Login fails  ", body = "Steps...", epicId = epic.Id });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var ticket = await ReadAsync<TicketDto>(created);
        ticket.TeamId.Should().Be(ctx.TeamId);
        ticket.Type.Should().Be("bug");
        ticket.State.Should().Be("new", "state defaults to new (A15)");
        ticket.Title.Should().Be("Login fails", "title is trimmed");
        ticket.Body.Should().Be("Steps...");
        ticket.EpicId.Should().Be(epic.Id);
        ticket.EpicTitle.Should().Be("Billing Revamp");
        ticket.CreatedBy.Should().Be(ctx.UserId, "created_by is the authenticated user (V18)");
        ticket.CreatedByEmail.Should().NotBeNullOrEmpty();
        ticket.CreatedAt.Should().Be(ticket.ModifiedAt);
    }

    [Fact]
    public async Task Create_with_explicit_state_is_honored()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "feature", title = "T", body = "B", state = "in_progress" }));
        ticket.State.Should().Be("in_progress");
    }

    [Theory]
    [InlineData("Bug")]
    [InlineData("task")]
    [InlineData("epic")]
    [InlineData("")]
    public async Task Create_with_invalid_type_is_400(string type)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type, title = "T", body = "B" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("type");
    }

    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    [InlineData("READY")]
    public async Task Create_with_invalid_state_is_400(string state)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("state");
    }

    [Theory]
    [InlineData("   ", "Body present")]
    [InlineData("Title present", "   ")]
    public async Task Create_with_blank_title_or_body_is_400(string title, string body)
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title, body });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("validation_error");
    }

    [Fact]
    public async Task Create_with_unknown_team_is_400()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var resp = await client.PostAsJsonAsync("/api/tickets",
            new { teamId = Guid.NewGuid(), type = "bug", title = "T", body = "B" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("teamId");
    }

    [Fact]
    public async Task Create_with_epic_from_a_different_team_is_400_epic_team_mismatch()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var payments = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Payments" }));
        var paymentsEpic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId = payments.Id, title = "Other" }));

        var resp = await client.PostAsJsonAsync("/api/tickets",
            new { teamId = platform.Id, type = "bug", title = "T", body = "B", epicId = paymentsEpic.Id });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("epic_team_mismatch");
    }

    [Fact]
    public async Task Update_changing_only_state_advances_modified_at()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(15));
        var updated = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = "in_progress", priority = "medium", epicId = (Guid?)null }));

        updated.State.Should().Be("in_progress");
        updated.ModifiedAt.Should().BeAfter(ticket.ModifiedAt, "an actual change advances modified_at (V19)");
    }

    [Fact]
    public async Task Update_with_no_changes_does_not_advance_modified_at()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "Login fails", body = "Steps..." }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(15));
        // Same values (title/body re-sent, whitespace-padded to exercise normalization). Priority is
        // required in the edit body and re-sent at its default so the whole update is a true no-op.
        var result = await ReadAsync<TicketDto>(await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = ctx.TeamId, type = "bug", title = "  Login fails  ", body = " Steps... ", state = "new", priority = "medium", epicId = (Guid?)null }));

        result.ModifiedAt.Should().Be(ticket.ModifiedAt, "saving unchanged values must not advance modified_at (V20, EC6)");
    }

    [Theory]
    [InlineData("type", "story")]
    [InlineData("state", "archived")]
    public async Task Update_with_invalid_enum_is_400(string field, string badValue)
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        object body = field == "type"
            ? new { teamId = ctx.TeamId, type = badValue, title = "T", body = "B", state = "new", priority = "medium", epicId = (Guid?)null }
            : new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B", state = badValue, priority = "medium", epicId = (Guid?)null };

        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Update_with_unknown_team_is_400()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        var resp = await ctx.Client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = Guid.NewGuid(), type = "bug", title = "T", body = "B", state = "new", priority = "medium", epicId = (Guid?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("teamId");
    }

    [Fact]
    public async Task Update_changing_team_but_keeping_cross_team_epic_is_400_epic_team_mismatch()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var payments = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Payments" }));
        var platformEpic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId = platform.Id, title = "Billing Revamp" }));

        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = platform.Id, type = "bug", title = "T", body = "B", epicId = platformEpic.Id }));

        // Force team=Payments while keeping the Platform epic (bypassing the UI which would clear it).
        var resp = await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = payments.Id, type = "bug", title = "T", body = "B", state = "new", priority = "medium", epicId = platformEpic.Id });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("epic_team_mismatch");

        // Sanity: nothing changed — the ticket is still on Platform with its original epic.
        var unchanged = await ReadAsync<TicketDto>(await client.GetAsync($"/api/tickets/{ticket.Id}"));
        unchanged.TeamId.Should().Be(platform.Id);
        unchanged.EpicId.Should().Be(platformEpic.Id);
    }

    [Fact]
    public async Task Update_changing_team_and_clearing_epic_succeeds()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var payments = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Payments" }));
        var platformEpic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId = platform.Id, title = "Billing Revamp" }));

        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = platform.Id, type = "bug", title = "T", body = "B", epicId = platformEpic.Id }));

        var updated = await ReadAsync<TicketDto>(await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = payments.Id, type = "bug", title = "T", body = "B", state = "new", priority = "medium", epicId = (Guid?)null }));
        updated.TeamId.Should().Be(payments.Id);
        updated.EpicId.Should().BeNull("clearing the epic on team change is accepted");
    }

    [Fact]
    public async Task Update_changing_team_and_choosing_a_valid_same_team_epic_succeeds()
    {
        var (token, _, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var platform = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var payments = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Payments" }));
        var paymentsEpic = await ReadAsync<EpicDto>(
            await client.PostAsJsonAsync("/api/epics", new { teamId = payments.Id, title = "Pay Epic" }));

        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = platform.Id, type = "bug", title = "T", body = "B" }));

        var updated = await ReadAsync<TicketDto>(await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new { teamId = payments.Id, type = "bug", title = "T", body = "B", state = "new", priority = "medium", epicId = paymentsEpic.Id }));
        updated.TeamId.Should().Be(payments.Id);
        updated.EpicId.Should().Be(paymentsEpic.Id);
    }

    [Fact]
    public async Task Patch_state_advances_modified_at_and_persists()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var patch = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/state", new { state = "done" });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await ReadAsync<TicketStateDto>(patch);
        state.State.Should().Be("done");
        state.ModifiedAt.Should().BeAfter(ticket.ModifiedAt);

        // Persisted: a fresh GET reflects the new state.
        var reloaded = await ReadAsync<TicketDto>(await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}"));
        reloaded.State.Should().Be("done");
    }

    [Fact]
    public async Task Patch_state_with_invalid_value_is_400()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));

        var patch = await ctx.Client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/state", new { state = "frozen" });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(patch)).Code.Should().Be("validation_error");
    }

    [Fact]
    public async Task Delete_ticket_cascades_to_its_comments()
    {
        var ctx = await SetupAsync();
        var ticket = await ReadAsync<TicketDto>(await ctx.Client.PostAsJsonAsync("/api/tickets",
            new { teamId = ctx.TeamId, type = "bug", title = "T", body = "B" }));
        await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "first" });
        await ctx.Client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { body = "second" });

        var del = await ctx.Client.DeleteAsync($"/api/tickets/{ticket.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Ticket is gone (404), and its comments went with it (the comments endpoint 404s on the missing ticket).
        (await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Client.GetAsync($"/api/tickets/{ticket.Id}/comments")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_unknown_ticket_is_404()
    {
        var ctx = await SetupAsync();
        (await ctx.Client.GetAsync($"/api/tickets/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
