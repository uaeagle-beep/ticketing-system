using System.Net;
using Microsoft.Extensions.Options;
using TicketTracker.Application.Options;
using TicketTracker.Infrastructure.Webhooks;

namespace TicketTracker.Tests.Unit;

/// <summary>
/// SSRF policy unit tests for <see cref="WebhookUrlValidator"/> (Wave 3, ADR-0021, §7.4). Verifies the two
/// gates with the prod-safe default (<c>AllowInsecure=false</c>): subscribe-time rejects non-https/malformed
/// URLs; send-time BLOCKS loopback / private (10/8, 172.16/12, 192.168/16) / link-local incl. cloud metadata
/// (169.254.169.254) / IPv6 loopback + ULA, while ALLOWING a public literal IP. With the escape hatch on
/// (as the integration test factory sets it) both gates relax so tests/local can target http/localhost.
/// Also asserts the shared <see cref="WebhookUrlValidator.IsBlockedAddress"/> classifier directly — the ONE
/// block-list the send-time pre-check AND the connect-pinning ConnectCallback (SEC-2) both use.
/// </summary>
public sealed class WebhookUrlValidatorTests
{
    private static WebhookUrlValidator Make(bool allowInsecure)
        => new(Options.Create(new WebhookOptions { AllowInsecure = allowInsecure }));

    // ---- subscribe-time scheme/format gate (prod-safe) ----

    [Theory]
    [InlineData("https://example.com/hook", true)]
    [InlineData("http://example.com/hook", false)]   // http rejected when insecure escape hatch is off
    [InlineData("not-a-url", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateForSubscribe_enforces_https_when_secure(string? url, bool expected)
    {
        var ok = Make(allowInsecure: false).ValidateForSubscribe(url, out var error);
        ok.Should().Be(expected);
        if (!expected)
            error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateForSubscribe_allows_http_when_insecure_escape_hatch_on()
        => Make(allowInsecure: true).ValidateForSubscribe("http://localhost:9000/hook", out _).Should().BeTrue();

    // ---- send-time private/loopback/metadata block (anti-DNS-rebind) ----

    [Theory]
    [InlineData("https://127.0.0.1/hook")]              // loopback
    [InlineData("https://10.0.0.5/hook")]               // private 10/8
    [InlineData("https://172.16.0.1/hook")]             // private 172.16/12
    [InlineData("https://192.168.1.1/hook")]            // private 192.168/16
    [InlineData("https://169.254.169.254/latest/meta")] // link-local cloud metadata
    [InlineData("https://[::1]/hook")]                  // IPv6 loopback
    [InlineData("https://[fc00::1]/hook")]              // IPv6 ULA
    public async Task IsAllowedAtSendTime_blocks_internal_targets(string url)
        => (await Make(allowInsecure: false).IsAllowedAtSendTimeAsync(url, CancellationToken.None))
            .Should().BeFalse();

    [Fact]
    public async Task IsAllowedAtSendTime_allows_a_public_literal_ip()
        => (await Make(allowInsecure: false).IsAllowedAtSendTimeAsync("https://93.184.216.34/hook", CancellationToken.None))
            .Should().BeTrue();

    [Fact]
    public async Task IsAllowedAtSendTime_never_blocks_when_insecure_escape_hatch_on()
        => (await Make(allowInsecure: true).IsAllowedAtSendTimeAsync("https://127.0.0.1/hook", CancellationToken.None))
            .Should().BeTrue("the escape hatch lets tests/local target localhost");

    // ---- the shared IP-classifier (SEC-2): ONE block-list used by BOTH the send-time pre-check AND the
    // webhooks HttpClient's ConnectCallback (connect-pinning). Asserting it directly documents the contract
    // the ConnectCallback relies on; the callback itself is integration-level (no real sockets opened here).

    [Theory]
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("10.0.0.5")]         // private 10/8
    [InlineData("172.16.0.1")]       // private 172.16/12
    [InlineData("172.31.255.255")]   // private 172.16/12 upper bound
    [InlineData("192.168.1.1")]      // private 192.168/16
    [InlineData("169.254.169.254")]  // link-local cloud metadata
    [InlineData("0.0.0.0")]          // unspecified / this-network
    [InlineData("::1")]              // IPv6 loopback
    [InlineData("fc00::1")]          // IPv6 ULA
    [InlineData("fd00::1")]          // IPv6 ULA
    [InlineData("fe80::1")]          // IPv6 link-local
    [InlineData("::ffff:10.0.0.5")]  // IPv4-mapped IPv6 of a private v4 (unwrapped + re-checked)
    public void IsBlockedAddress_blocks_private_and_metadata_ranges(string ip)
        => WebhookUrlValidator.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeTrue();

    [Theory]
    [InlineData("93.184.216.34")]      // public IPv4 (example.com)
    [InlineData("8.8.8.8")]            // public IPv4
    [InlineData("172.15.0.1")]         // just OUTSIDE 172.16/12 → public
    [InlineData("172.32.0.1")]         // just OUTSIDE 172.16/12 → public
    [InlineData("2606:2800:220:1::")]  // public IPv6 (example.com)
    public void IsBlockedAddress_allows_public_addresses(string ip)
        => WebhookUrlValidator.IsBlockedAddress(IPAddress.Parse(ip)).Should().BeFalse();
}
