using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// QA acceptance suite for Attachments (Wave 3, ADR-0018, API_CONTRACT §5.2; test-guidance §11 A).
/// Presses the risk areas the developer smoke test only samples: the FULL magic-byte allowlist
/// (png/jpg/gif/webp/pdf/office/zip/csv/txt), the content-type spoof matrix (exe/svg/html posing as an
/// allowed type → 415), the 10 MB boundary (−1 ok, +1 → 413), 404-vs-403 anti-IDOR ordering on all four
/// endpoints, admin cross-team access, delete as a *different* team member (team-write, not uploader-only),
/// ticket-delete cascading attachments away, and storage-key opacity. Real HTTP over the in-memory SQLite
/// factory + in-memory attachment storage (no disk).
/// </summary>
public sealed class AttachmentsAcceptanceTests : IntegrationTestBase
{
    // ---- valid magic-byte payloads for each allowlisted type ----
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00");
    private static readonly byte[] Webp = BuildWebp();
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n");
    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 };
    private static readonly byte[] Ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] TextPlain = Encoding.UTF8.GetBytes("plain text content, no NUL");
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n");

    private static byte[] BuildWebp()
    {
        var b = new byte[16];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(b, 0);
        // bytes 4-7: file size (ignored by the sniff)
        Encoding.ASCII.GetBytes("WEBP").CopyTo(b, 8);
        Encoding.ASCII.GetBytes("VP8 ").CopyTo(b, 12);
        return b;
    }

    private sealed record Ctx(HttpClient Client, Guid UserId, Guid TeamId, Guid TicketId);

    private async Task<Ctx> SetupAsync()
    {
        var (token, userId, _) = await RegisterVerifiedUserAsync();
        var client = Authed(token);
        var team = await ReadAsync<TeamDto>(await client.PostAsJsonAsync("/api/teams", new { name = "Platform" }));
        var ticket = await ReadAsync<TicketDto>(await client.PostAsJsonAsync("/api/tickets",
            new { teamId = team.Id, type = "bug", title = "Login fails", body = "Steps" }));
        return new Ctx(client, userId, team.Id, ticket.Id);
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string filename, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { content, "file", filename } };
        return form;
    }

    private Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid ticketId, byte[] bytes, string filename, string contentType)
        => client.PostAsync($"/api/tickets/{ticketId}/attachments", FileForm(bytes, filename, contentType));

    // ================= Allowlist: every accepted type sniffs through =================

    public static IEnumerable<object[]> AllowedTypes() => new List<object[]>
    {
        new object[] { Png, "a.png", "image/png" },
        new object[] { Jpeg, "a.jpg", "image/jpeg" },
        new object[] { Gif, "a.gif", "image/gif" },
        new object[] { Webp, "a.webp", "image/webp" },
        new object[] { Pdf, "a.pdf", "application/pdf" },
        new object[] { Zip, "a.zip", "application/zip" },
        new object[] { Zip, "a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        new object[] { Zip, "a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        new object[] { Ole, "a.doc", "application/msword" },
        new object[] { Ole, "a.xls", "application/vnd.ms-excel" },
        new object[] { TextPlain, "a.txt", "text/plain" },
        new object[] { Csv, "a.csv", "text/csv" },
    };

    [Theory]
    [MemberData(nameof(AllowedTypes))]
    public async Task Upload_each_allowed_type_with_matching_magic_bytes_is_201(byte[] bytes, string filename, string contentType)
    {
        var ctx = await SetupAsync();
        var resp = await UploadAsync(ctx.Client, ctx.TicketId, bytes, filename, contentType);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, "{0} with correct magic bytes is on the allowlist", contentType);
        var dto = await ReadAsync<AttachmentDto>(resp);
        dto.ContentType.Should().Be(contentType);
        dto.SizeBytes.Should().Be(bytes.Length);
    }

    // ================= Spoof matrix: dangerous payloads posing as an allowed type → 415 =================

    public static IEnumerable<object[]> SpoofPayloads() => new List<object[]>
    {
        // an MZ/PE executable renamed as image/png
        new object[] { new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 }, "evil.png", "image/png" },
        // an ELF binary declared as application/pdf
        new object[] { new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 }, "evil.pdf", "application/pdf" },
        // an SVG (script vector) declared as image/png
        new object[] { Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "x.png", "image/png" },
        // an HTML doc declared as image/jpeg
        new object[] { Encoding.UTF8.GetBytes("<!doctype html><html><body>hi</body></html>"), "x.jpg", "image/jpeg" },
        // a shebang script declared as text/plain (dangerous even though text has no magic)
        new object[] { Encoding.UTF8.GetBytes("#!/bin/sh\nrm -rf /\n"), "run.txt", "text/plain" },
        // a real JPEG declared as image/png (magic mismatch between two allowed types)
        new object[] { Jpeg, "x.png", "image/png" },
    };

    [Theory]
    [MemberData(nameof(SpoofPayloads))]
    public async Task Upload_spoofed_or_dangerous_payload_is_415_and_stores_nothing(byte[] bytes, string filename, string contentType)
    {
        var ctx = await SetupAsync();
        var resp = await UploadAsync(ctx.Client, ctx.TicketId, bytes, filename, contentType);
        resp.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await ReadErrorAsync(resp)).Code.Should().Be("unsupported_media_type");
        Factory.AttachmentStorage.Keys.Should().BeEmpty("a rejected upload writes no blob");
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("application/x-msdownload")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    public async Task Upload_denied_declared_type_is_415(string deniedType)
    {
        var ctx = await SetupAsync();
        var resp = await UploadAsync(ctx.Client, ctx.TicketId, Png, "x.bin", deniedType);
        resp.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await ReadErrorAsync(resp)).Code.Should().Be("unsupported_media_type");
    }

    // ================= Size boundary: cap−1 ok, cap+1 → 413 =================

    private const int Cap = 10 * 1024 * 1024; // ATTACHMENTS_MAX_BYTES default (10 MB)

    [Fact]
    public async Task Upload_exactly_at_the_cap_is_accepted()
    {
        var ctx = await SetupAsync();
        var atCap = new byte[Cap];
        Png.CopyTo(atCap, 0); // valid PNG magic, padded to exactly the cap
        var resp = await UploadAsync(ctx.Client, ctx.TicketId, atCap, "big.png", "image/png");
        resp.StatusCode.Should().Be(HttpStatusCode.Created, "a file exactly at the cap is allowed (boundary)");
        (await ReadAsync<AttachmentDto>(resp)).SizeBytes.Should().Be(Cap);
    }

    [Fact]
    public async Task Upload_one_byte_over_the_cap_is_413()
    {
        var ctx = await SetupAsync();
        var overCap = new byte[Cap + 1];
        Png.CopyTo(overCap, 0);
        var resp = await UploadAsync(ctx.Client, ctx.TicketId, overCap, "big.png", "image/png");
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ReadErrorAsync(resp)).Code.Should().Be("payload_too_large");
        Factory.AttachmentStorage.Keys.Should().BeEmpty("the partial blob is deleted on overflow");
    }

    // ================= Anti-IDOR: 404 (unknown) BEFORE 403 (non-member) =================

    [Fact]
    public async Task Download_and_delete_unknown_id_is_404_even_for_a_non_member()
    {
        var ctx = await SetupAsync();
        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var outsider = Authed(outsiderToken);
        var unknown = Guid.NewGuid();

        // Unknown resource is 404 regardless of caller (resolve-then-check: absent → 404, not 403).
        (await outsider.GetAsync($"/api/attachments/{unknown}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/attachments/{unknown}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Existing_attachment_on_a_foreign_team_is_403_for_a_non_member()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx.Client, ctx.TicketId, Png, "s.png", "image/png"));

        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var outsider = Authed(outsiderToken);

        // The resource exists but the caller lacks team access → 403 (not 404).
        (await outsider.GetAsync($"/api/attachments/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/attachments/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/tickets/{ctx.TicketId}/attachments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================= Admin sees any team's attachments (ADR-0007) =================

    [Fact]
    public async Task Admin_can_list_download_and_delete_any_teams_attachment()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx.Client, ctx.TicketId, Png, "s.png", "image/png"));

        // A brand-new admin who is a member of NO team still reaches any team (admin override).
        var (adminToken, _, _) = await RegisterAdminAsync();
        var admin = Authed(adminToken);

        (await admin.GetAsync($"/api/tickets/{ctx.TicketId}/attachments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/attachments/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/api/attachments/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ================= Delete is team-write, NOT uploader-only ([ASSUMPTION W3-ATT-DELETE]) =================

    [Fact]
    public async Task A_different_team_member_can_delete_another_members_attachment()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx.Client, ctx.TicketId, Png, "s.png", "image/png"));

        // A second, non-admin member of the same team — team-write means they may delete a teammate's file.
        var (mateToken, mateId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(mateId, ctx.TeamId);
        var mate = Authed(mateToken);

        (await mate.DeleteAsync($"/api/attachments/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await ReadAsync<List<AttachmentDto>>(await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/attachments"));
        list.Should().BeEmpty();
    }

    // ================= Ticket delete cascades attachments (metadata) away =================

    [Fact]
    public async Task Deleting_the_ticket_cascades_its_attachment_metadata_rows()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx.Client, ctx.TicketId, Png, "s.png", "image/png"));

        (await ctx.Client.DeleteAsync($"/api/tickets/{ctx.TicketId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The attachment metadata row is gone (CASCADE); the endpoint that reads it now 404s.
        (await ctx.Client.GetAsync($"/api/attachments/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await Factory.WithDbAsync(async db =>
            (await db.Attachments.CountAsync(a => a.Id == dto.Id)).Should().Be(0));
    }

    // ================= List of an empty ticket is [] (not a crash) =================

    [Fact]
    public async Task List_on_a_ticket_with_no_attachments_is_empty_200()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/attachments");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<List<AttachmentDto>>(resp)).Should().BeEmpty();
    }

    // ================= Storage key opacity + never leaks the storage key =================

    [Fact]
    public async Task Metadata_never_exposes_the_storage_key_and_the_disk_key_is_opaque()
    {
        var ctx = await SetupAsync();
        var raw = await UploadAsync(ctx.Client, ctx.TicketId, Png, "my secret filename.png", "image/png");
        var json = await raw.Content.ReadAsStringAsync();

        json.Should().NotContain("storageKey", "the storage key is server-internal and never serialized");
        json.Should().NotContain("StorageKey");

        var key = Factory.AttachmentStorage.Keys.Single();
        key.Should().MatchRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}$", "the on-disk key is date-sharded + guid, path-traversal-proof");
        key.Should().NotContain("secret filename", "the client filename is never part of the disk key");
    }
}
