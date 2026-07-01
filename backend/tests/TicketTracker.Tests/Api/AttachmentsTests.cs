using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Tests.Infrastructure;

namespace TicketTracker.Tests.Api;

/// <summary>
/// Developer smoke tests for attachments (Wave 3, ADR-0018, API_CONTRACT §5.2). Samples the load-bearing
/// behaviours — upload happy path, oversize → 413, disallowed/spoofed type → 415, forced-download headers,
/// team-scope 403/404, delete removes row + blob, and the event backbone wiring (attachment_added →
/// activity + notification to watchers; attachment_deleted → activity only). Full acceptance coverage is
/// the Tester's job. Real HTTP over the in-memory SQLite factory with the in-memory attachment storage.
/// </summary>
public sealed class AttachmentsTests : IntegrationTestBase
{
    // A minimal valid PNG header + a few bytes (magic bytes must sniff as image/png).
    private static readonly byte[] PngBytes =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
    };

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
        var form = new MultipartFormDataContent();
        form.Add(content, "file", filename);
        return form;
    }

    private Task<HttpResponseMessage> UploadAsync(Ctx ctx, byte[] bytes, string filename, string contentType)
        => ctx.Client.PostAsync($"/api/tickets/{ctx.TicketId}/attachments", FileForm(bytes, filename, contentType));

    // ---- Upload happy path ----

    [Fact]
    public async Task Upload_allowed_type_returns_201_with_metadata_and_stores_blob()
    {
        var ctx = await SetupAsync();

        var resp = await UploadAsync(ctx, PngBytes, "screenshot.png", "image/png");
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await ReadAsync<AttachmentDto>(resp);
        dto.Filename.Should().Be("screenshot.png");
        dto.ContentType.Should().Be("image/png");
        dto.SizeBytes.Should().Be(PngBytes.Length);
        dto.UploadedBy.Should().Be(ctx.UserId);
        dto.TicketId.Should().Be(ctx.TicketId);
        dto.CreatedAt.Should().Be(Factory.Clock.UtcNow);

        // The blob is in storage under an opaque, date-sharded key (never the client filename).
        Factory.AttachmentStorage.Keys.Should().ContainSingle();
        var key = Factory.AttachmentStorage.Keys.Single();
        key.Should().NotContain("screenshot", "the on-disk key is server-generated + opaque, not the filename");
        key.Should().MatchRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Upload_appears_in_the_ticket_attachment_list_chronologically()
    {
        var ctx = await SetupAsync();

        await UploadAsync(ctx, PngBytes, "a.png", "image/png");
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await UploadAsync(ctx, PngBytes, "b.png", "image/png");

        var list = await ReadAsync<List<AttachmentDto>>(
            await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/attachments"));
        list.Select(a => a.Filename).Should().ContainInOrder("a.png", "b.png");
    }

    // ---- Size cap → 413 ----

    [Fact]
    public async Task Upload_over_the_size_cap_is_413_and_stores_nothing()
    {
        var ctx = await SetupAsync();

        // Default cap is 10 MB; send 11 MB of PNG-prefixed bytes so the cap (not the type) trips.
        var big = new byte[11 * 1024 * 1024];
        Array.Copy(PngBytes, big, PngBytes.Length);

        var resp = await UploadAsync(ctx, big, "big.png", "image/png");
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ReadErrorAsync(resp)).Code.Should().Be("payload_too_large");

        Factory.AttachmentStorage.Keys.Should().BeEmpty("the partial blob is deleted on overflow");
        var list = await ReadAsync<List<AttachmentDto>>(
            await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/attachments"));
        list.Should().BeEmpty("no metadata row is written for an over-cap upload");
    }

    // ---- Disallowed / spoofed type → 415 ----

    [Fact]
    public async Task Upload_disallowed_type_is_415()
    {
        var ctx = await SetupAsync();
        var html = System.Text.Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>");

        var resp = await UploadAsync(ctx, html, "evil.html", "text/html");
        resp.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await ReadErrorAsync(resp)).Code.Should().Be("unsupported_media_type");
    }

    [Fact]
    public async Task Upload_spoofed_magic_bytes_is_415()
    {
        var ctx = await SetupAsync();
        // An HTML/script payload declared as image/png — the magic-byte sniff rejects the spoof.
        var spoof = System.Text.Encoding.UTF8.GetBytes("<html><body>not an image</body></html>");

        var resp = await UploadAsync(ctx, spoof, "fake.png", "image/png");
        resp.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await ReadErrorAsync(resp)).Code.Should().Be("unsupported_media_type");
        Factory.AttachmentStorage.Keys.Should().BeEmpty("nothing is written when the sniff rejects the payload");
    }

    // ---- Download: correct bytes + forced-download headers ----

    [Fact]
    public async Task Download_returns_bytes_with_attachment_disposition_and_nosniff()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx, PngBytes, "screenshot.png", "image/png"));

        var resp = await ctx.Client.GetAsync($"/api/attachments/{dto.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        resp.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        resp.Content.Headers.ContentDisposition!.DispositionType.Should().NotBe("inline");
        resp.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.Should().Contain("nosniff");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(PngBytes);
    }

    // ---- Team-scope: 404 (unknown) and 403 (non-member) ----

    [Fact]
    public async Task Upload_to_unknown_ticket_is_404()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsync($"/api/tickets/{Guid.NewGuid()}/attachments",
            FileForm(PngBytes, "a.png", "image/png"));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_unknown_attachment_is_404()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.GetAsync($"/api/attachments/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Non_member_cannot_list_download_or_delete_a_teams_attachments_403()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx, PngBytes, "s.png", "image/png"));

        // A verified member of NO team (not admin) — must not reach another team's attachments.
        var (outsiderToken, _, _) = await RegisterMemberAsync();
        var outsider = Authed(outsiderToken);

        (await outsider.GetAsync($"/api/tickets/{ctx.TicketId}/attachments")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/attachments/{dto.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/attachments/{dto.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await outsider.PostAsync($"/api/tickets/{ctx.TicketId}/attachments",
            FileForm(PngBytes, "x.png", "image/png"))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Delete: removes row + blob, 204 ----

    [Fact]
    public async Task Delete_removes_row_and_blob_and_returns_204()
    {
        var ctx = await SetupAsync();
        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx, PngBytes, "s.png", "image/png"));
        var key = Factory.AttachmentStorage.Keys.Single();

        var del = await ctx.Client.DeleteAsync($"/api/attachments/{dto.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Factory.AttachmentStorage.Contains(key).Should().BeFalse("the blob is best-effort deleted");
        var list = await ReadAsync<List<AttachmentDto>>(
            await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/attachments"));
        list.Should().BeEmpty("the metadata row is removed");

        (await ctx.Client.GetAsync($"/api/attachments/{dto.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Event backbone wiring ----

    [Fact]
    public async Task Upload_writes_activity_and_notifies_watchers_but_not_the_actor()
    {
        var ctx = await SetupAsync();

        // A second team member watches the ticket; the uploader (ctx.UserId) is the actor.
        var (watcherToken, watcherId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(watcherId, ctx.TeamId);
        var watcher = Authed(watcherToken);
        (await watcher.PostAsync($"/api/tickets/{ctx.TicketId}/watch", null)).EnsureSuccessStatusCode();

        await UploadAsync(ctx, PngBytes, "screenshot.png", "image/png");

        // Activity records the event.
        var activity = await ReadAsync<ActivityListDto>(
            await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/activity"));
        activity.Items.Should().Contain(a => a.EventType == "attachment_added"
            && a.Summary.Contains("screenshot.png"));

        // The watcher (not the actor) is notified.
        var watcherNotes = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        watcherNotes.Items.Should().ContainSingle(n => n.EventType == "attachment_added");

        var actorNotes = await ReadAsync<NotificationListDto>(await ctx.Client.GetAsync("/api/notifications"));
        actorNotes.Items.Should().NotContain(n => n.EventType == "attachment_added",
            "the uploader is never notified about their own upload");
    }

    [Fact]
    public async Task Delete_writes_activity_only_no_notification()
    {
        var ctx = await SetupAsync();

        var (watcherToken, watcherId, _) = await RegisterMemberAsync();
        await AddMembershipAsync(watcherId, ctx.TeamId);
        var watcher = Authed(watcherToken);
        (await watcher.PostAsync($"/api/tickets/{ctx.TicketId}/watch", null)).EnsureSuccessStatusCode();

        var dto = await ReadAsync<AttachmentDto>(await UploadAsync(ctx, PngBytes, "s.png", "image/png"));
        (await ctx.Client.DeleteAsync($"/api/attachments/{dto.Id}")).EnsureSuccessStatusCode();

        var activity = await ReadAsync<ActivityListDto>(
            await ctx.Client.GetAsync($"/api/tickets/{ctx.TicketId}/activity"));
        activity.Items.Should().Contain(a => a.EventType == "attachment_deleted");

        var watcherNotes = await ReadAsync<NotificationListDto>(await watcher.GetAsync("/api/notifications"));
        watcherNotes.Items.Should().NotContain(n => n.EventType == "attachment_deleted",
            "attachment_deleted is activity-only (mirrors comment_deleted)");
    }

    // ---- 400 on empty / missing file ----

    [Fact]
    public async Task Upload_with_no_file_part_is_400()
    {
        var ctx = await SetupAsync();
        var resp = await ctx.Client.PostAsync($"/api/tickets/{ctx.TicketId}/attachments",
            new MultipartFormDataContent());
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
