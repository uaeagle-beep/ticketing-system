using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Labels/tags (Wave 2, ADR-0016, WAVE2 §5.6/§5.7/§8, test guidance §10.H). Team-scoped, member-managed
/// CRUD; per-team case-insensitive uniqueness (409 duplicate_label_name); color validation (400 keyed
/// color); disposable delete (removes from all tickets via cascade); full-set-replace label assignment on a
/// ticket (400 keyed labelIds for a cross-team label); and the board &amp;labelId= filter. IDOR: label ops on a
/// non-member team → 403 (resolve-then-check). Smoke coverage; the full feature suite is the Tester's.
/// Real HTTP over the in-memory SQLite factory.
/// </summary>
public sealed class LabelsTests : IntegrationTestBase
{
    private sealed record World(HttpClient Admin, Guid TeamId);

    private async Task<World> SetupAsync(string teamName = "Platform")
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = teamName }));
        return new World(admin, team.Id);
    }

    private async Task<LabelDto> CreateLabelAsync(HttpClient client, Guid teamId, string name, string color = "#3b82f6")
    {
        var resp = await client.PostAsJsonAsync("/api/labels", new { teamId, name, color });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadAsync<LabelDto>(resp);
    }

    private async Task<TicketDto> CreateTicketAsync(HttpClient client, Guid teamId, string title = "T")
        => await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId, type = "bug", title, body = "B" }));

    // ---------------- CRUD ----------------

    [Fact]
    public async Task Member_can_create_label_returns_201_with_lowercased_color()
    {
        var w = await SetupAsync();
        // A plain member of the team (not an admin) may create labels (ADR-0016).
        var (memberToken, memberId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(memberId, w.TeamId);
        var member = Authed(memberToken);

        var resp = await member.PostAsJsonAsync("/api/labels",
            new { teamId = w.TeamId, name = "  Backend  ", color = "#3B82F6" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var label = await ReadAsync<LabelDto>(resp);
        label.TeamId.Should().Be(w.TeamId);
        label.Name.Should().Be("Backend", "the display name is trimmed");
        label.Color.Should().Be("#3b82f6", "the color is lowercased");
    }

    [Fact]
    public async Task Duplicate_name_in_same_team_is_409_case_insensitive()
    {
        var w = await SetupAsync();
        await CreateLabelAsync(w.Admin, w.TeamId, "Bug");

        var resp = await w.Admin.PostAsJsonAsync("/api/labels", new { teamId = w.TeamId, name = "bug", color = "#111111" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("duplicate_label_name");
    }

    [Fact]
    public async Task Same_name_in_different_team_is_allowed()
    {
        var w = await SetupAsync();
        var other = await ReadAsync<TeamDto>(await w.Admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        await CreateLabelAsync(w.Admin, w.TeamId, "bug");

        // The same normalized name in a DIFFERENT team is fine (per-team uniqueness).
        var resp = await w.Admin.PostAsJsonAsync("/api/labels", new { teamId = other.Id, name = "bug", color = "#222222" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Bad_color_is_400_keyed_color()
    {
        var w = await SetupAsync();
        var resp = await w.Admin.PostAsJsonAsync("/api/labels", new { teamId = w.TeamId, name = "X", color = "blue" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("color");
    }

    [Fact]
    public async Task Blank_name_is_400_keyed_name()
    {
        var w = await SetupAsync();
        var resp = await w.Admin.PostAsJsonAsync("/api/labels", new { teamId = w.TeamId, name = "   ", color = "#123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task List_returns_team_labels_ordered_by_name()
    {
        var w = await SetupAsync();
        await CreateLabelAsync(w.Admin, w.TeamId, "zeta");
        await CreateLabelAsync(w.Admin, w.TeamId, "Alpha");

        var labels = await ReadAsync<List<LabelDto>>(await w.Admin.GetAsync($"/api/labels?teamId={w.TeamId}"));
        labels.Select(l => l.Name).Should().ContainInOrder("Alpha", "zeta");
    }

    [Fact]
    public async Task Rename_recolor_returns_200()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "Old");

        var resp = await w.Admin.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "New", color = "#2563eb" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadAsync<LabelDto>(resp);
        updated.Name.Should().Be("New");
        updated.Color.Should().Be("#2563eb");
    }

    [Fact]
    public async Task Rename_to_existing_other_label_is_409()
    {
        var w = await SetupAsync();
        await CreateLabelAsync(w.Admin, w.TeamId, "Bug");
        var feature = await CreateLabelAsync(w.Admin, w.TeamId, "Feature");

        var resp = await w.Admin.PutAsJsonAsync($"/api/labels/{feature.Id}", new { name = "bug", color = "#333333" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(resp)).Code.Should().Be("duplicate_label_name");
    }

    // ---------------- Delete (disposable, removes from all tickets) ----------------

    [Fact]
    public async Task Delete_removes_label_and_its_ticket_associations()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "Bug");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { label.Id } });

        var del = await w.Admin.DeleteAsync($"/api/labels/{label.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The label is gone from the ticket detail (its ticket_labels rows cascaded away).
        var detail = await ReadAsync<TicketDto>(await w.Admin.GetAsync($"/api/tickets/{ticket.Id}"));
        detail.Labels.Should().BeEmpty();

        // And no orphan ticket_labels rows remain.
        var tagCount = 0;
        await Factory.WithDbAsync(async db =>
            tagCount = await db.TicketLabels.CountAsync(tl => tl.LabelId == label.Id));
        tagCount.Should().Be(0);
    }

    // ---------------- Assign labels to a ticket (full-set replace) ----------------

    [Fact]
    public async Task Set_labels_full_replace_and_dedup()
    {
        var w = await SetupAsync();
        var l1 = await CreateLabelAsync(w.Admin, w.TeamId, "a");
        var l2 = await CreateLabelAsync(w.Admin, w.TeamId, "b");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        // Duplicates in the body are de-duplicated.
        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels",
            new { labelIds = new[] { l1.Id, l2.Id, l1.Id } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadAsync<TicketDto>(resp);
        detail.Labels!.Select(l => l.Id).Should().BeEquivalentTo(new[] { l1.Id, l2.Id });
        detail.Labels!.Should().Contain(l => l.Id == l1.Id && l.Color == "#3b82f6", "the chip carries the color");

        // A subsequent replace with only l2 removes l1 (full-set replace).
        var resp2 = await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { l2.Id } });
        var detail2 = await ReadAsync<TicketDto>(resp2);
        detail2.Labels!.Select(l => l.Id).Should().BeEquivalentTo(new[] { l2.Id });

        // Null/empty clears all.
        var resp3 = await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = Array.Empty<Guid>() });
        (await ReadAsync<TicketDto>(resp3)).Labels.Should().BeEmpty();
    }

    [Fact]
    public async Task Cross_team_label_is_400_keyed_labelIds()
    {
        var w = await SetupAsync();
        var other = await ReadAsync<TeamDto>(await w.Admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        var foreignLabel = await CreateLabelAsync(w.Admin, other.Id, "foreign");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { foreignLabel.Id } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("labelIds");
    }

    [Fact]
    public async Task Set_labels_does_not_bump_modified_at()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "a");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);
        var before = ticket.ModifiedAt;

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var detail = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { label.Id } }));

        detail.ModifiedAt.Should().Be(before, "labels are metadata and never bump modified_at (§5.7)");
    }

    // ---------------- Board filter ----------------

    [Fact]
    public async Task Board_labelId_filter_subsets_correctly()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "a");
        var tagged = await CreateTicketAsync(w.Admin, w.TeamId, "tagged");
        var untagged = await CreateTicketAsync(w.Admin, w.TeamId, "untagged");
        await w.Admin.PutAsJsonAsync($"/api/tickets/{tagged.Id}/labels", new { labelIds = new[] { label.Id } });

        var board = await ReadAsync<BoardDto>(
            await w.Admin.GetAsync($"/api/tickets?teamId={w.TeamId}&labelId={label.Id}"));

        var ids = board.Columns.SelectMany(c => c.Tickets).Select(t => t.Id).ToList();
        ids.Should().Contain(tagged.Id);
        ids.Should().NotContain(untagged.Id);
        board.Total.Should().Be(1);
    }

    [Fact]
    public async Task Board_unknown_labelId_matches_nothing()
    {
        var w = await SetupAsync();
        await CreateTicketAsync(w.Admin, w.TeamId);
        var board = await ReadAsync<BoardDto>(
            await w.Admin.GetAsync($"/api/tickets?teamId={w.TeamId}&labelId={Guid.NewGuid()}"));
        board.Total.Should().Be(0);
    }

    // ---------------- IDOR / authz ----------------

    [Fact]
    public async Task Label_ops_on_non_member_team_are_403()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "a");

        // An outsider who is a member of a DIFFERENT team cannot see/manage this team's labels.
        var otherTeam = await ReadAsync<TeamDto>(await w.Admin.PostAsJsonAsync("/api/teams", new { name = "Outside" }));
        var (outsiderToken, outsiderId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(outsiderId, otherTeam.Id);
        var outsider = Authed(outsiderToken);

        (await outsider.GetAsync($"/api/labels?teamId={w.TeamId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.PostAsJsonAsync("/api/labels", new { teamId = w.TeamId, name = "z", color = "#123456" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.PutAsJsonAsync($"/api/labels/{label.Id}", new { name = "z", color = "#123456" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/labels/{label.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_label_is_404()
    {
        var w = await SetupAsync();
        (await w.Admin.PutAsJsonAsync($"/api/labels/{Guid.NewGuid()}", new { name = "x", color = "#123456" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await w.Admin.DeleteAsync($"/api/labels/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Missing_teamId_on_list_is_400()
    {
        var w = await SetupAsync();
        (await w.Admin.GetAsync("/api/labels")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var w = await SetupAsync();
        (await Client.GetAsync($"/api/labels?teamId={w.TeamId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
