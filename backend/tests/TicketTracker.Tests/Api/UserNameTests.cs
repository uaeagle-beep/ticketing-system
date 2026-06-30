using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Tests for the optional display-name feature (USER_MANAGEMENT_DESIGN Feature 1). Proves, over real
/// HTTP: admin create with/without a name; admin set/clear via PUT /name; validation (too long → 400
/// keyed "name", whitespace → null); and that the name surfaces in /me, the admin list, a ticket's
/// createdByName and a comment's authorName. Email always remains the login/account key.
/// </summary>
public sealed class UserNameTests : IntegrationTestBase
{
    // ============================================================== Create

    [Fact]
    public async Task Admin_creates_user_with_a_name_and_it_is_stored_and_returned()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "named@dataart.com", isAdmin = false, name = "  Ada Lovelace  " });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync<CreateUserResponseDto>(create);
        body.User.Name.Should().Be("Ada Lovelace", "the name is trimmed and stored");
        body.User.Email.Should().Be("named@dataart.com", "email stays the account key");
    }

    [Fact]
    public async Task Admin_creates_user_without_a_name_stores_null()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "noname@dataart.com", isAdmin = false });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync<CreateUserResponseDto>(create)).User.Name.Should().BeNull();
    }

    [Fact]
    public async Task Admin_creates_user_with_whitespace_name_stores_null()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "ws@dataart.com", isAdmin = false, name = "   " });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync<CreateUserResponseDto>(create)).User.Name.Should().BeNull(
            "a whitespace-only name normalizes to null");
    }

    [Fact]
    public async Task Admin_create_with_name_too_long_is_400_validation_error_keyed_name()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var create = await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "long@dataart.com", isAdmin = false, name = new string('x', 101) });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(create);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("name");
    }

    // ============================================================== Set / clear (PUT /name)

    [Fact]
    public async Task Admin_sets_then_clears_a_users_name()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, _) = await RegisterMemberAsync();

        // Set.
        var set = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/name", new { name = "Grace Hopper" });
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(set)).Name.Should().Be("Grace Hopper");

        // Clear (null).
        var clearNull = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/name", new { name = (string?)null });
        clearNull.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(clearNull)).Name.Should().BeNull();

        // Re-set then clear with whitespace.
        await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/name", new { name = "Grace" });
        var clearWs = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/name", new { name = "   " });
        clearWs.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AdminUserDto>(clearWs)).Name.Should().BeNull("whitespace clears the name");
    }

    [Fact]
    public async Task Set_name_too_long_is_400_validation_error_keyed_name()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var (_, memberId, _) = await RegisterMemberAsync();

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{memberId}/name",
            new { name = new string('y', 101) });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Set_name_on_unknown_user_is_404_not_found()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        var resp = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/name", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Set_name_by_a_member_is_403_forbidden()
    {
        var (_, memberId, _) = await RegisterMemberAsync();
        var (memberToken, _, _) = await RegisterMemberAsync();
        var member = Authed(memberToken);

        var resp = await member.PutAsJsonAsync($"/api/admin/users/{memberId}/name", new { name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(resp)).Code.Should().Be("forbidden");
    }

    // ============================================================== Surfaced everywhere

    [Fact]
    public async Task Name_is_present_in_me_and_admin_list()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/name", new { name = "Boss Admin" });

        // /me carries the name.
        var me = await ReadAsync<UserDto>(await admin.GetAsync("/api/auth/me"));
        me.Name.Should().Be("Boss Admin");

        // The admin list carries the name (and email) for every user.
        var users = await ReadAsync<List<AdminUserDto>>(await admin.GetAsync("/api/admin/users"));
        users.Single(u => u.Id == adminId).Name.Should().Be("Boss Admin");
    }

    [Fact]
    public async Task Login_response_user_carries_the_name()
    {
        // Create a user with a name + chosen password, then log them in and assert the bootstrap name.
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PostAsJsonAsync("/api/admin/users",
            new { email = "login-name@dataart.com", password = "chosen-password-123", isAdmin = false, name = "Linus" });

        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "login-name@dataart.com", password = "chosen-password-123" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<LoginDto>(login)).User.Name.Should().Be("Linus");
    }

    [Fact]
    public async Task Ticket_detail_carries_createdByName()
    {
        // Admin with a name creates a team + ticket; the ticket detail reflects the creator's name.
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/name", new { name = "Ticket Creator" });
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));

        var detail = await ReadAsync<TicketDto>(await admin.GetAsync($"/api/tickets/{ticket.Id}"));
        detail.CreatedByName.Should().Be("Ticket Creator");
        detail.CreatedByEmail.Should().NotBeNullOrWhiteSpace("email remains present alongside the name");
    }

    [Fact]
    public async Task Ticket_detail_createdByName_is_null_when_creator_has_no_name()
    {
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));

        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));

        (await ReadAsync<TicketDto>(await admin.GetAsync($"/api/tickets/{ticket.Id}")))
            .CreatedByName.Should().BeNull();
    }

    [Fact]
    public async Task Comment_carries_authorName_on_list_and_create()
    {
        var (adminToken, adminId, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);
        await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/name", new { name = "Comment Author" });
        var team = await ReadAsync<TeamDto>(await admin.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await admin.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "T", body = "B" }));

        // Create returns the author's name.
        var created = await ReadAsync<CommentDto>(await admin.PostAsJsonAsync(
            $"/api/tickets/{ticket.Id}/comments", new { body = "Looks fixed." }));
        created.AuthorName.Should().Be("Comment Author");
        created.AuthorEmail.Should().NotBeNullOrWhiteSpace();

        // List returns the author's name too.
        var list = await ReadAsync<List<CommentDto>>(await admin.GetAsync($"/api/tickets/{ticket.Id}/comments"));
        list.Should().ContainSingle().Which.AuthorName.Should().Be("Comment Author");
    }
}
