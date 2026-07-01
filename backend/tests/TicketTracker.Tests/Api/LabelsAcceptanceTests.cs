using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite — labels, extending the developer smoke tests (WAVE2 §10 H, §5.6/§5.7, ADR-0016).
/// Presses gaps: unknown label id in the assign set → 400 keyed labelIds; oversize name → 400 keyed name;
/// create against an unknown team → 404 (per API_CONTRACT §7b.2); PUT tickets/{id}/labels on a foreign-team
/// ticket → 404-then-403 ordering; a plain member can assign labels; delete cascade removes the label from
/// EVERY tagged ticket with no orphan rows; the board &amp;labelId= filter combines with other filters; and
/// label assignment never bumps the ticket's modified_at while a real field edit still does.
/// </summary>
public sealed class LabelsAcceptanceTests : IntegrationTestBase
{
    private sealed record World(HttpClient Admin, Guid AdminId, Guid TeamId);

    private async Task<World> SetupAsync(string teamName = "Platform")
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = teamName }));
        return new World(admin, adminId, team.Id);
    }

    private async Task<LabelDto> CreateLabelAsync(HttpClient client, Guid teamId, string name, string color = "#3b82f6")
        => await ReadAsync<LabelDto>(await client.PostAsJsonAsync("/api/labels", new { teamId, name, color }));

    private async Task<TicketDto> CreateTicketAsync(HttpClient client, Guid teamId, string title = "T")
        => await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId, type = "bug", title, body = "B" }));

    [Fact]
    public async Task Unknown_label_id_in_the_assign_set_is_400_keyed_labelIds()
    {
        var w = await SetupAsync();
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels",
            new { labelIds = new[] { Guid.NewGuid() } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await ReadErrorAsync(resp);
        err.Code.Should().Be("validation_error");
        err.Errors.Should().ContainKey("labelIds");
    }

    [Fact]
    public async Task Mixed_valid_and_unknown_label_ids_reject_the_whole_set_400()
    {
        var w = await SetupAsync();
        var good = await CreateLabelAsync(w.Admin, w.TeamId, "good");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        var resp = await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels",
            new { labelIds = new[] { good.Id, Guid.NewGuid() } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Nothing was applied (all-or-nothing validation before mutation).
        var detail = await ReadAsync<TicketDto>(await w.Admin.GetAsync($"/api/tickets/{ticket.Id}"));
        detail.Labels.Should().BeEmpty("a rejected set leaves the ticket's labels unchanged");
    }

    [Fact]
    public async Task Oversize_name_is_400_keyed_name()
    {
        var w = await SetupAsync();
        var tooLong = new string('x', 51); // LabelNameMax = 50
        var resp = await w.Admin.PostAsJsonAsync("/api/labels", new { teamId = w.TeamId, name = tooLong, color = "#123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Create_against_unknown_team_is_404_per_contract()
    {
        var w = await SetupAsync();
        var resp = await w.Admin.PostAsJsonAsync("/api/labels",
            new { teamId = Guid.NewGuid(), name = "x", color = "#123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound, "API_CONTRACT §7b.2: unknown team → 404");
    }

    [Fact]
    public async Task Assign_labels_on_foreign_team_ticket_is_404_then_403()
    {
        var w = await SetupAsync();
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        // A member of a DIFFERENT team can neither see nor label this ticket → 403 (resolve-then-check).
        var otherTeam = await ReadAsync<TeamDto>(await w.Admin.PostAsJsonAsync("/api/teams", new { name = "Other" }));
        var (outsiderToken, outsiderId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(outsiderId, otherTeam.Id);
        var outsider = Authed(outsiderToken);

        (await outsider.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = Array.Empty<Guid>() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Unknown ticket → 404.
        (await w.Admin.PutAsJsonAsync($"/api/tickets/{Guid.NewGuid()}/labels", new { labelIds = Array.Empty<Guid>() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Plain_member_can_assign_labels_to_a_ticket()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "a");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);

        var (memberToken, memberId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(memberId, w.TeamId);
        var member = Authed(memberToken);

        var resp = await member.PutAsJsonAsync($"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { label.Id } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<TicketDto>(resp)).Labels.Should().ContainSingle(l => l.Id == label.Id);
    }

    [Fact]
    public async Task Delete_removes_the_label_from_every_tagged_ticket_no_orphans()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "shared");
        var t1 = await CreateTicketAsync(w.Admin, w.TeamId, "one");
        var t2 = await CreateTicketAsync(w.Admin, w.TeamId, "two");
        var t3 = await CreateTicketAsync(w.Admin, w.TeamId, "three");
        foreach (var t in new[] { t1, t2, t3 })
            (await w.Admin.PutAsJsonAsync($"/api/tickets/{t.Id}/labels", new { labelIds = new[] { label.Id } })).EnsureSuccessStatusCode();

        (await w.Admin.DeleteAsync($"/api/labels/{label.Id}")).EnsureSuccessStatusCode();

        foreach (var t in new[] { t1, t2, t3 })
        {
            var detail = await ReadAsync<TicketDto>(await w.Admin.GetAsync($"/api/tickets/{t.Id}"));
            detail.Labels.Should().BeEmpty($"the deleted label is removed from {t.Title}");
        }
        await Factory.WithDbAsync(async db =>
            (await db.TicketLabels.CountAsync(tl => tl.LabelId == label.Id)).Should().Be(0, "no orphan ticket_labels remain"));
    }

    [Fact]
    public async Task Board_labelId_filter_combines_with_type_filter()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "a");

        var bugTagged = await ReadAsync<TicketDto>(await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "bug", title = "bug+label", body = "B" }));
        var featureTagged = await ReadAsync<TicketDto>(await w.Admin.PostAsJsonAsync("/api/tickets",
            new { teamId = w.TeamId, type = "feature", title = "feature+label", body = "B" }));
        foreach (var t in new[] { bugTagged, featureTagged })
            (await w.Admin.PutAsJsonAsync($"/api/tickets/{t.Id}/labels", new { labelIds = new[] { label.Id } })).EnsureSuccessStatusCode();

        // labelId AND type=bug → only the bug-tagged ticket.
        var board = await ReadAsync<BoardDto>(
            await w.Admin.GetAsync($"/api/tickets?teamId={w.TeamId}&labelId={label.Id}&type=bug"));
        var ids = board.Columns.SelectMany(c => c.Tickets).Select(t => t.Id).ToList();
        ids.Should().Contain(bugTagged.Id);
        ids.Should().NotContain(featureTagged.Id, "the label filter ANDs with the type filter");
        board.Total.Should().Be(1);
    }

    [Fact]
    public async Task Label_assignment_does_not_bump_modified_at_but_a_field_edit_does()
    {
        var w = await SetupAsync();
        var label = await CreateLabelAsync(w.Admin, w.TeamId, "a");
        var ticket = await CreateTicketAsync(w.Admin, w.TeamId);
        var before = ticket.ModifiedAt;

        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var afterLabels = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/labels", new { labelIds = new[] { label.Id } }));
        afterLabels.ModifiedAt.Should().Be(before, "labels are metadata; assignment never bumps modified_at (§J)");

        // A real field edit DOES bump modified_at (control).
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var edited = await ReadAsync<TicketDto>(await w.Admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}", new
        {
            teamId = w.TeamId, type = "bug", title = "Renamed", body = "B", state = "new", priority = "medium"
        }));
        edited.ModifiedAt.Should().BeAfter(before, "a scalar field edit still advances modified_at");
    }
}
