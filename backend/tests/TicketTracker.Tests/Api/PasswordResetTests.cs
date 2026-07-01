using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Fakes;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// F-01 Self-service password reset (WAVE1_DESIGN §8.D, ADR-0010). Two public endpoints:
/// <c>POST /api/auth/forgot-password</c> (non-enumerating 202 — identical for unknown / unverified /
/// blocked / verified, no link issued in the no-op cases) and <c>POST /api/auth/reset-password</c>
/// (single-use, 1h expiry via clock advance, purges ALL sessions, owner-blocked-after-issuance ⇒
/// token invalid). <see cref="FakeEmailSender.LastFor(string, FakeEmailSender.EmailKind)"/> captures
/// the reset link so the raw token can be extracted offline. Reset is PUBLIC, so no bearer session is
/// needed for the reset call — the clock can advance freely past the token TTL. Real HTTP, SQLite.
/// </summary>
public sealed class PasswordResetTests : IntegrationTestBase
{
    private const string OldPassword = "old correct horse";
    private const string NewPassword = "new correct horse!";

    /// <summary>Signup + verify a fresh account (no login). Returns (userId, email).</summary>
    private async Task<(Guid UserId, string Email)> SignupVerifyAsync(string email, string password = OldPassword)
    {
        (await Client.PostAsJsonAsync("/api/auth/signup", new { email, password }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var verifyLink = Factory.Email.LastFor(email, FakeEmailSender.EmailKind.Verification);
        verifyLink.Should().NotBeNull();
        var verifyToken = FakeEmailSender.ExtractToken(verifyLink!.Link);
        (await Client.PostAsJsonAsync("/api/auth/verify-email", new { token = verifyToken }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        Guid userId = Guid.Empty;
        await Factory.WithDbAsync(async db =>
        {
            var u = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.Users, x => x.EmailNormalized == email.Trim().ToLowerInvariant());
            userId = u.Id;
        });
        return (userId, email);
    }

    private async Task<string> RequestResetAndCaptureTokenAsync(string email)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, "forgot-password always returns 202 (§4.4)");
        var link = Factory.Email.LastFor(email, FakeEmailSender.EmailKind.PasswordReset);
        link.Should().NotBeNull("a reset link is issued for a verified, non-blocked account");
        return FakeEmailSender.ExtractToken(link!.Link);
    }

    // ---------------- forgot-password: happy path ----------------

    [Fact]
    public async Task Forgot_password_for_a_verified_user_returns_202_and_captures_a_reset_link()
    {
        var (_, email) = await SignupVerifyAsync("reset-happy@dataart.com");
        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        Factory.Email.LastFor(email, FakeEmailSender.EmailKind.PasswordReset)
            .Should().NotBeNull("a reset link is issued and captured for a verified account");
    }

    // ---------------- reset-password: happy path + session purge ----------------

    [Fact]
    public async Task Reset_with_a_valid_token_sets_the_new_password_old_fails_new_works()
    {
        var (_, email) = await SignupVerifyAsync("reset-flow@dataart.com");
        var token = await RequestResetAndCaptureTokenAsync(email);

        var reset = await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = NewPassword });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        // Old password no longer works.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = OldPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old password is invalid after reset");
        // New password works.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the new password logs in after reset");
    }

    [Fact]
    public async Task Reset_purges_all_existing_sessions()
    {
        var (_, email) = await SignupVerifyAsync("reset-purge@dataart.com");
        // Establish a live session BEFORE the reset.
        var login = await ReadAsync<LoginDto>(
            await Client.PostAsJsonAsync("/api/auth/login", new { email, password = OldPassword }));
        var pre = Authed(login.Token);
        (await pre.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "session works before reset");

        var token = await RequestResetAndCaptureTokenAsync(email);
        (await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The pre-existing session is now dead (ALL sessions purged, §6.1).
        (await pre.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a reset purges ALL of the user's sessions (pre-existing token ⇒ 401)");
    }

    // ---------------- single-use + expiry ----------------

    [Fact]
    public async Task Reset_token_is_single_use()
    {
        var (_, email) = await SignupVerifyAsync("reset-once@dataart.com");
        var token = await RequestResetAndCaptureTokenAsync(email);

        (await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A second use of the same (now consumed) token is rejected.
        var second = await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "another pass 1" });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(second)).Code.Should().Be("invalid_or_expired_token", "single-use (§4.4)");
    }

    [Fact]
    public async Task Reset_token_expires_after_one_hour()
    {
        var (_, email) = await SignupVerifyAsync("reset-expiry@dataart.com");
        var token = await RequestResetAndCaptureTokenAsync(email);

        // Advance past the 1h TTL (PASSWORD_RESET_TTL_HOURS default 1). Boundary now >= expires_at (A31).
        Factory.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = NewPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("invalid_or_expired_token", "expired token ⇒ 400 (§4.4)");

        // The password was NOT changed — the old one still logs in.
        (await Client.PostAsJsonAsync("/api/auth/login", new { email, password = OldPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Requesting_a_new_reset_invalidates_the_prior_unused_token()
    {
        var (_, email) = await SignupVerifyAsync("reset-reissue@dataart.com");
        var firstToken = await RequestResetAndCaptureTokenAsync(email);
        var secondToken = await RequestResetAndCaptureTokenAsync(email);
        firstToken.Should().NotBe(secondToken);

        // The first (now superseded) token must no longer work (at most one live reset token, V4).
        var withFirst = await Client.PostAsJsonAsync("/api/auth/reset-password", new { token = firstToken, password = NewPassword });
        withFirst.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(withFirst)).Code.Should().Be("invalid_or_expired_token");

        // The latest token works.
        (await Client.PostAsJsonAsync("/api/auth/reset-password", new { token = secondToken, password = NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------- non-enumeration: unknown / unverified / blocked no-ops ----------------

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_is_202_with_no_link()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@dataart.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, "identical 202 for an unknown email (non-enumeration)");
        Factory.Email.LastFor("nobody@dataart.com", FakeEmailSender.EmailKind.PasswordReset)
            .Should().BeNull("no reset link is issued for an unknown email");
    }

    [Fact]
    public async Task Forgot_password_for_an_unverified_account_is_202_with_no_link()
    {
        // Signup but do NOT verify.
        const string email = "reset-unverified@dataart.com";
        (await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = OldPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Factory.Email.LastFor(email, FakeEmailSender.EmailKind.PasswordReset)
            .Should().BeNull("an unverified account is a silent no-op — no reset link (§6.1, ASSUMPTION W1-RESET-UNVERIFIED)");
    }

    [Fact]
    public async Task Forgot_password_for_a_blocked_account_is_202_with_no_link()
    {
        var (userId, email) = await SignupVerifyAsync("reset-blocked@dataart.com");
        await BlockUserAsync(userId);

        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Factory.Email.LastFor(email, FakeEmailSender.EmailKind.PasswordReset)
            .Should().BeNull("a blocked account is a silent no-op — no reset link (§6.1, ASSUMPTION W1-RESET-BLOCKED)");
    }

    [Fact]
    public async Task Forgot_password_with_a_blank_email_is_202_with_no_link()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "   " });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Factory.Email.Sent.Should().NotContain(e => e.Kind == FakeEmailSender.EmailKind.PasswordReset,
            "a blank email issues no reset link");
    }

    // ---------------- blocked-after-issuance defence-in-depth ----------------

    [Fact]
    public async Task Reset_is_rejected_if_the_owner_is_blocked_after_the_token_was_issued()
    {
        var (userId, email) = await SignupVerifyAsync("reset-block-after@dataart.com");
        var token = await RequestResetAndCaptureTokenAsync(email);

        // Block the user AFTER the token was issued (defence-in-depth, §6.1).
        await BlockUserAsync(userId);

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password = NewPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("invalid_or_expired_token",
            "a token whose owner became blocked is treated as invalid (§4.4)");
    }

    // ---------------- reset input validation ----------------

    [Theory]
    [InlineData("short")]   // < 8 chars
    [InlineData("1234567")] // 7 chars
    public async Task Reset_with_a_too_short_new_password_is_400_keyed_password(string password)
    {
        var (_, email) = await SignupVerifyAsync($"reset-shortpw-{password.Length}@dataart.com");
        var token = await RequestResetAndCaptureTokenAsync(email);

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password", new { token, password });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("password");
    }

    [Fact]
    public async Task Reset_with_an_unknown_token_is_400_invalid_or_expired()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = "totally-made-up-token", password = NewPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Code.Should().Be("invalid_or_expired_token");
    }
}
