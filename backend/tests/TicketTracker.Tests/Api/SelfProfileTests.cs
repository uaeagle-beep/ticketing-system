using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// F-04 Self-service profile (WAVE1_DESIGN §8.E, ADR-0010). <c>PUT /api/me/profile</c> sets/clears the
/// caller's own display name (>100 ⇒ 400 keyed <c>name</c>; blank/whitespace ⇒ null; idempotent);
/// <c>POST /api/me/password</c> changes the caller's own password with CURRENT-password re-auth (wrong
/// current ⇒ 401 invalid_credentials; too-short new ⇒ 400 keyed <c>newPassword</c>; 204 on success;
/// the CURRENT session stays valid; OTHER sessions are purged). Self-only BY CONSTRUCTION: there is no
/// user id in the path, so a user cannot address another account (no cross-user route). Real HTTP, SQLite.
/// </summary>
public sealed class SelfProfileTests : IntegrationTestBase
{
    private const string Password = "correct horse battery";

    // ---------------- PUT /api/me/profile: name set / clear / validate ----------------

    [Fact]
    public async Task Update_profile_sets_the_display_name_and_returns_it()
    {
        var (token, _, _) = await RegisterMemberAsync("profile-set@dataart.com");
        var me = Authed(token);

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name = "  Alex Doe  " });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await ReadAsync<UserDto>(resp);
        user.Name.Should().Be("Alex Doe", "the name is trimmed and returned in the UserDto (§4.5)");

        // Persisted: /me reflects it.
        (await ReadAsync<UserDto>(await me.GetAsync("/api/auth/me"))).Name.Should().Be("Alex Doe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_profile_with_blank_or_null_clears_the_name(string? name)
    {
        var (token, _, _) = await RegisterMemberAsync($"profile-clear-{(name ?? "null").Length}@dataart.com");
        var me = Authed(token);
        await me.PutAsJsonAsync("/api/me/profile", new { name = "Set First" });

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<UserDto>(resp)).Name.Should().BeNull("blank/whitespace/null clears the name to null (§4.5)");
    }

    [Fact]
    public async Task Update_profile_with_name_over_100_chars_is_400_keyed_name()
    {
        var (token, _, _) = await RegisterMemberAsync("profile-toolong@dataart.com");
        var me = Authed(token);

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name = new string('x', 101) });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Update_profile_at_exactly_100_chars_is_allowed()
    {
        var (token, _, _) = await RegisterMemberAsync("profile-boundary@dataart.com");
        var me = Authed(token);

        var name = new string('y', 100);
        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "the 100-char boundary is inclusive (NameMax = 100)");
        (await ReadAsync<UserDto>(resp)).Name.Should().Be(name);
    }

    [Fact]
    public async Task Update_profile_requires_authentication()
    {
        (await Client.PutAsJsonAsync("/api/me/profile", new { name = "Nobody" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no session ⇒ 401 (§4.5)");
    }

    // ---------------- PUT /api/me/profile: locale set / clear / validate (Wave 3 i18n, §5.7) ----------------

    [Fact]
    public async Task Me_returns_locale_null_for_a_fresh_user()
    {
        var (token, _, _) = await RegisterMemberAsync("locale-default@dataart.com");
        var me = Authed(token);

        (await ReadAsync<UserDto>(await me.GetAsync("/api/auth/me"))).Locale
            .Should().BeNull("a fresh user has no persisted locale — the SPA falls back to detection / the uk default (ADR-0022)");
    }

    [Theory]
    [InlineData("uk")]
    [InlineData("en")]
    public async Task Update_profile_sets_a_valid_locale_and_returns_it(string locale)
    {
        var (token, _, _) = await RegisterMemberAsync($"locale-set-{locale}@dataart.com");
        var me = Authed(token);

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name = (string?)null, locale });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<UserDto>(resp)).Locale.Should().Be(locale, "a supported locale is persisted and returned in the UserDto (§5.7)");

        // Persisted: /me reflects it (cross-device bootstrap source, ADR-0022).
        (await ReadAsync<UserDto>(await me.GetAsync("/api/auth/me"))).Locale.Should().Be(locale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_profile_with_blank_or_null_locale_clears_it(string? locale)
    {
        var (token, _, _) = await RegisterMemberAsync($"locale-clear-{(locale ?? "null").Length}@dataart.com");
        var me = Authed(token);
        await me.PutAsJsonAsync("/api/me/profile", new { name = (string?)null, locale = "uk" });

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name = (string?)null, locale });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<UserDto>(resp)).Locale.Should().BeNull("blank/whitespace/null clears the locale to null = unset (§5.7)");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("EN")]
    [InlineData("en-US")]
    [InlineData("xx")]
    public async Task Update_profile_with_unsupported_locale_is_400_keyed_locale(string locale)
    {
        var (token, _, _) = await RegisterMemberAsync($"locale-bad-{locale}@dataart.com");
        var me = Authed(token);

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name = (string?)null, locale });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("locale", "an unsupported locale is a 400 keyed locale (§5.7)");
    }

    [Fact]
    public async Task Update_profile_can_set_name_and_locale_together_without_clobbering_either()
    {
        var (token, _, _) = await RegisterMemberAsync("locale-and-name@dataart.com");
        var me = Authed(token);

        var resp = await me.PutAsJsonAsync("/api/me/profile", new { name = "Оксана", locale = "uk" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await ReadAsync<UserDto>(resp);
        user.Name.Should().Be("Оксана");
        user.Locale.Should().Be("uk");
    }

    // ---------------- Self-only by construction: no cross-user route ----------------

    [Fact]
    public async Task There_is_no_route_to_edit_another_users_profile_via_me()
    {
        var (token, _, _) = await RegisterMemberAsync("self-only@dataart.com");
        var (_, otherId, _) = await RegisterMemberAsync("other-target@dataart.com");
        var me = Authed(token);

        // Probing an id-bearing path under /api/me must NOT exist (no cross-user surface, ADR-0010 §D).
        var withId = await me.PutAsJsonAsync($"/api/me/{otherId}/profile", new { name = "Hijack" });
        withId.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed },
            "there is no id-addressable self route — the target is always the authenticated principal");

        // And the other user's name is unchanged.
        var (otherToken, _, _) = await RegisterMemberAsync("other-target2@dataart.com");
        var other = Authed(otherToken);
        (await ReadAsync<UserDto>(await other.GetAsync("/api/auth/me"))).Name.Should().BeNull();
    }

    // ---------------- POST /api/me/password: re-auth, session hygiene ----------------

    [Fact]
    public async Task Change_password_with_correct_current_returns_204_and_keeps_current_session()
    {
        var (token, _, email) = await RegisterMemberAsync("pw-change@dataart.com");
        var me = Authed(token);

        var resp = await me.PostAsJsonAsync("/api/me/password",
            new { currentPassword = Password, newPassword = "brand new password" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, "204 on success, no body (§4.5)");

        // The CURRENT session is still valid.
        (await me.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the caller's current session stays valid after a self password change (§4.5)");

        // The new password logs in; the old one does not.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = "brand new password" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_purges_other_sessions_but_not_the_current_one()
    {
        var (currentToken, _, email) = await RegisterMemberAsync("pw-sessions@dataart.com");

        // A SECOND independent session for the same user (a different device).
        var otherLogin = await ReadAsync<LoginDto>(
            await Client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }));
        var otherDevice = Authed(otherLogin.Token);
        (await otherDevice.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "other session works before");

        var me = Authed(currentToken);
        (await me.PostAsJsonAsync("/api/me/password",
                new { currentPassword = Password, newPassword = "yet another password" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The OTHER session is now purged; the CURRENT one survives (§4.5, ASSUMPTION W1-PROFILE-PWD-SESSIONS).
        (await otherDevice.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "other sessions are purged on a self password change");
        (await me.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the current session is kept");
    }

    [Fact]
    public async Task Change_password_with_wrong_current_is_401_invalid_credentials()
    {
        var (token, _, _) = await RegisterMemberAsync("pw-wrong@dataart.com");
        var me = Authed(token);

        var resp = await me.PostAsJsonAsync("/api/me/password",
            new { currentPassword = "not my password", newPassword = "a valid new password" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(resp)).Code.Should().Be("invalid_credentials",
            "current-password re-auth failure is a credentials failure (§6.2)");
    }

    [Fact]
    public async Task Change_password_with_wrong_current_does_not_change_the_password()
    {
        var (token, _, email) = await RegisterMemberAsync("pw-wrong-nochange@dataart.com");
        var me = Authed(token);

        await me.PostAsJsonAsync("/api/me/password",
            new { currentPassword = "wrong", newPassword = "a valid new password" });

        // The original password still logs in — nothing changed.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task Change_password_with_too_short_new_is_400_keyed_newPassword(string newPassword)
    {
        var (token, _, _) = await RegisterMemberAsync($"pw-shortnew-{newPassword.Length}@dataart.com");
        var me = Authed(token);

        var resp = await me.PostAsJsonAsync("/api/me/password",
            new { currentPassword = Password, newPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("newPassword");
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        (await Client.PostAsJsonAsync("/api/me/password",
                new { currentPassword = Password, newPassword = "a valid new password" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Blocked_user_mid_session_is_401_at_the_middleware_on_me_endpoints()
    {
        var (token, userId, _) = await RegisterMemberAsync("pw-blocked@dataart.com");
        var me = Authed(token);
        (await me.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Block mid-session; the middleware treats blocked == not authenticated (§4.5).
        await BlockUserAsync(userId);

        (await me.PutAsJsonAsync("/api/me/profile", new { name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a blocked user is 401 at the middleware");
        (await me.PostAsJsonAsync("/api/me/password", new { currentPassword = Password, newPassword = "new valid pw here" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
