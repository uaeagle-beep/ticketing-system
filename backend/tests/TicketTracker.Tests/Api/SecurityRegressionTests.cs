using System.Net;
using System.Net.Http.Json;
using TicketTracker.Tests.Fakes;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Targeted regression coverage for the AUTH-* / SCT-* security fixes (QA, additive — does not
/// weaken existing tests):
///   • AUTH-001 equal-cost anti-enumeration: login with a never-registered email still returns the
///     same 401 invalid_credentials (the new dummy-Argon2 verify on the null-user branch must not
///     change the externally observable outcome).
///   • AUTH-004 / SCT-005 keyed (HMAC-SHA256) token hashing: the full signup → verify → login flow
///     still works end-to-end under the keyed pepper. Both the emailed verification token AND the
///     issued session token are stored as HMAC hashes; this proves issue-time and verify/lookup-time
///     hashing stay symmetric (a broken/asymmetric key would make verify-email and bearer auth miss).
/// </summary>
public sealed class SecurityRegressionTests : IntegrationTestBase
{
    [Fact]
    public async Task Login_with_unknown_email_returns_401_invalid_credentials_AUTH_001()
    {
        // No signup for this address: exercises the null-user branch that now runs a real
        // Argon2id verify against a fixed dummy hash before throwing. Outcome must be unchanged.
        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "ghost-never-registered@dataart.com", password = DefaultPassword });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadErrorAsync(login)).Code.Should().Be("invalid_credentials");

        // And nothing was created/emailed as a side effect of a failed login.
        Factory.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Full_signup_verify_login_flow_works_under_keyed_HMAC_token_hashing_AUTH_004()
    {
        const string email = "hmac-regression@dataart.com";

        // signup → a verification email with a raw token is captured
        var signup = await Client.PostAsJsonAsync("/api/auth/signup",
            new { email, password = DefaultPassword });
        signup.StatusCode.Should().Be(HttpStatusCode.Created);

        var captured = Factory.Email.LastFor(email);
        captured.Should().NotBeNull();
        var rawToken = FakeEmailSender.ExtractToken(captured!.Link);

        // verify-email re-hashes the raw token with the SAME keyed HMAC used at issue time and must
        // find the stored hash — proving the pepper is applied symmetrically (would 400 otherwise).
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token = rawToken });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        // login issues a session token whose hash is also HMAC-keyed
        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new { email, password = DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await ReadAsync<LoginDto>(login);
        loginBody.Token.Should().NotBeNullOrWhiteSpace();

        // bearer auth re-hashes the raw session token with the keyed HMAC for the session lookup;
        // a successful protected call proves session issue/verify hashing is symmetric too.
        var authed = Authed(loginBody.Token);
        var me = await authed.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<UserDto>(me)).Email.Should().Be(email);
    }
}
