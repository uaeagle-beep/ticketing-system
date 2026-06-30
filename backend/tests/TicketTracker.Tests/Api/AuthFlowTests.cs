using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TicketTracker.Tests.Fakes;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Authentication &amp; email-verification business flow over real HTTP (E1, API_CONTRACT §3).
/// Covers: signup → capture link → verify (single-use; re-use fails; expired fails via clock;
/// resend invalidates prior) → login → bearer access → logout → 401; password &lt; 8 → 400;
/// duplicate email (case-insensitive, non-enumerating); unauthenticated → 401; unverified → 403.
/// </summary>
public sealed class AuthFlowTests : IntegrationTestBase
{

    [Fact]
    public async Task Signup_then_verify_then_login_grants_bearer_access_and_logout_revokes_it()
    {
        const string email = "alex@dataart.com";

        // --- signup: 201, no session, verification email captured (FRONTEND_URL/verify-email?token=RAW)
        var signup = await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        signup.StatusCode.Should().Be(HttpStatusCode.Created);
        var signupBody = await ReadAsync<MessageDto>(signup);
        signupBody.Message.Should().Contain("verify");

        var captured = Factory.Email.LastFor(email);
        captured.Should().NotBeNull();
        captured!.Link.Should().StartWith("http://localhost:8080/verify-email?token=");

        // --- verify: 200, single-use token consumed
        var rawToken = FakeEmailSender.ExtractToken(captured.Link);
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token = rawToken });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        // --- login: 200 with token + verified user
        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await ReadAsync<LoginDto>(login);
        loginBody.Token.Should().NotBeNullOrWhiteSpace();
        loginBody.User.Email.Should().Be(email);
        loginBody.User.EmailVerified.Should().BeTrue();

        // --- bearer access to a business endpoint works
        var authed = Authed(loginBody.Token);
        var me = await authed.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<UserDto>(me)).Email.Should().Be(email);

        var teamsOk = await authed.GetAsync("/api/teams");
        teamsOk.StatusCode.Should().Be(HttpStatusCode.OK);

        // --- logout: 204, then the same token is 401 (EC15)
        var logout = await authed.PostAsync("/api/auth/logout", content: null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterLogout = await authed.GetAsync("/api/teams");
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(afterLogout)).Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Verify_is_single_use_second_use_fails_with_invalid_or_expired_token()
    {
        const string email = "single-use@dataart.com";
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        var token = FakeEmailSender.ExtractToken(Factory.Email.LastFor(email)!.Link);

        var first = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(second)).Code.Should().Be("invalid_or_expired_token");
    }

    [Fact]
    public async Task Verify_with_expired_token_fails_after_24h_boundary()
    {
        const string email = "expired@dataart.com";
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        var token = FakeEmailSender.ExtractToken(Factory.Email.LastFor(email)!.Link);

        // A31: now >= expires_at => expired. Token issued at 12:00 with TTL 24h, so advance to +24h exactly.
        Factory.Clock.Advance(TimeSpan.FromHours(24));

        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(verify)).Code.Should().Be("invalid_or_expired_token");
    }

    [Fact]
    public async Task Verify_just_before_24h_boundary_still_succeeds()
    {
        const string email = "almost-expired@dataart.com";
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        var token = FakeEmailSender.ExtractToken(Factory.Email.LastFor(email)!.Link);

        // One minute before expiry must still verify (sanity: the expiry test isn't trivially passing).
        Factory.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));

        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resend_invalidates_the_previous_unused_token()
    {
        const string email = "resend@dataart.com";
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        var firstToken = FakeEmailSender.ExtractToken(Factory.Email.LastFor(email)!.Link);

        // Resend issues T2 and invalidates T1 (V4). Advance a little so T2's link differs/captured after.
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var resend = await Client.PostAsJsonAsync("/api/auth/resend-verification", new { email });
        resend.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var secondCapture = Factory.Email.LastFor(email)!;
        var secondToken = FakeEmailSender.ExtractToken(secondCapture.Link);
        secondToken.Should().NotBe(firstToken, "resend issues a brand new token");

        // T1 (the old token) can no longer verify.
        var useOld = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token = firstToken });
        useOld.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(useOld)).Code.Should().Be("invalid_or_expired_token");

        // T2 (the new token) verifies.
        var useNew = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token = secondToken });
        useNew.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resend_for_unknown_email_is_non_committal_202_and_sends_nothing()
    {
        var resend = await Client.PostAsJsonAsync("/api/auth/resend-verification",
            new { email = "nobody@dataart.com" });
        resend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Factory.Email.Sent.Should().BeEmpty("no account exists, so no usable token is emailed (A8)");
    }

    [Fact]
    public async Task Signup_with_password_shorter_than_8_is_rejected_400()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/signup",
            new { email = "short@dataart.com", password = "1234567" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(resp);
        error.Code.Should().Be("validation_error");
        error.Errors.Should().ContainKey("password");

        // Sanity: no account was created — login must fail as invalid_credentials, not 403 unverified.
        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "short@dataart.com", password = "1234567" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signup_with_blank_email_is_rejected_400()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/signup",
            new { email = "   ", password = DefaultPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(resp)).Errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task Duplicate_email_signup_is_case_and_trim_insensitive_and_non_enumerating()
    {
        const string email = "dup@dataart.com";
        var first = await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        Factory.Email.Sent.Should().HaveCount(1);

        // A case/trim variant of the same email must NOT create a second account (V1, A6) and,
        // per API_CONTRACT §3.1, the response is the same non-enumerating 201 with no new email.
        var dup = await Client.PostAsJsonAsync("/api/auth/signup",
            new { email = "  DUP@DataArt.com  ", password = DefaultPassword });
        dup.StatusCode.Should().Be(HttpStatusCode.Created);
        Factory.Email.Sent.Should().HaveCount(1, "no second account is created, so no second email is sent");
    }

    [Fact]
    public async Task Login_is_case_insensitive_and_trimmed_on_email()
    {
        const string email = "casetest@dataart.com";
        await RegisterVerifiedUserAsync(email);

        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "  CASETEST@DataArt.com ", password = DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_401_invalid_credentials()
    {
        const string email = "wrongpw@dataart.com";
        await RegisterVerifiedUserAsync(email);

        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "totally-wrong-password" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(login)).Code.Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Login_unverified_account_is_403_account_not_verified_and_issues_no_session()
    {
        const string email = "unverified@dataart.com";
        // Signup only — do NOT verify.
        await Client.PostAsJsonAsync("/api/auth/signup", new { email, password = DefaultPassword });

        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorAsync(login)).Code.Should().Be("account_not_verified");
    }

    [Theory]
    [InlineData("/api/teams")]
    [InlineData("/api/epics?teamId=" + "00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/tickets?teamId=" + "00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/auth/me")]
    public async Task Business_endpoints_without_a_token_return_401(string path)
    {
        var resp = await Client.GetAsync(path);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(resp)).Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Invalid_bearer_token_is_rejected_401()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        var resp = await client.GetAsync("/api/teams");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
