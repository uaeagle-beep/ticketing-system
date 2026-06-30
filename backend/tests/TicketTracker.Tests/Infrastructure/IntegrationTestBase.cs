using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TicketTracker.Tests.Infrastructure;

/// <summary>
/// Base class for WebApplicationFactory-backed HTTP integration tests. Each TEST gets its own
/// factory instance, and therefore its own fresh in-memory SQLite database, capturing
/// FakeEmailSender and TestClock — so tests are fully isolated and order-independent (ADR-0002:
/// "tests do not share state"). The factory is in-process and EnsureCreated() is sub-second, so
/// per-test construction is cheap. Provides high-level flow helpers (signup → capture link →
/// verify → login) and thin typed HTTP wrappers so the tests read as the specification, not as
/// plumbing. All calls go over real HTTP through HttpClient — no service is invoked directly.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly CustomWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected IntegrationTestBase()
    {
        Factory = new CustomWebApplicationFactory();
        Factory.Clock.SetUtcNow(new DateTime(2026, 06, 30, 12, 00, 00, DateTimeKind.Utc));
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    // ---------- Auth flow helpers ----------

    protected const string DefaultPassword = "correct horse battery";

    /// <summary>Signup, extract the emailed token, verify, and log in. Returns (token, userId, email).</summary>
    protected async Task<(string Token, Guid UserId, string Email)> RegisterVerifiedUserAsync(
        string? email = null, string password = DefaultPassword)
    {
        email ??= $"user-{Guid.NewGuid():N}@dataart.com";

        var signup = await Client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        signup.StatusCode.Should().Be(HttpStatusCode.Created,
            "signup of a fresh email returns 201 (API_CONTRACT §3.1)");

        var captured = Factory.Email.LastFor(email.Trim());
        captured.Should().NotBeNull("a verification email must have been captured for the new account");
        var token = Fakes.FakeEmailSender.ExtractToken(captured!.Link);

        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        verify.StatusCode.Should().Be(HttpStatusCode.OK, "the fresh token verifies the account");

        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, "a verified account can log in");
        var body = await ReadAsync<LoginDto>(login);
        return (body.Token, body.User.Id, body.User.Email);
    }

    /// <summary>An HttpClient with the given bearer token attached (no auth attached on the base client).</summary>
    protected HttpClient Authed(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---------- Typed HTTP wrappers ----------

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var value = await JsonSerializer.DeserializeAsync<T>(stream, Json);
        value.Should().NotBeNull("response body for {0} should deserialize to {1}",
            response.RequestMessage?.RequestUri, typeof(T).Name);
        return value!;
    }

    protected static async Task<ErrorBodyDto> ReadErrorAsync(HttpResponseMessage response)
    {
        var envelope = await ReadAsync<ErrorEnvelopeDto>(response);
        envelope.Error.Should().NotBeNull("non-2xx responses use the uniform error envelope (API_CONTRACT §2)");
        return envelope.Error;
    }
}
