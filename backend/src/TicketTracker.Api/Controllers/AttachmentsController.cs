using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TicketTracker.Application.Common;
using TicketTracker.Application.Dtos;
using TicketTracker.Application.Services;

namespace TicketTracker.Api.Controllers;

/// <summary>
/// Attachment upload/list (ticket sub-resource) and download/delete (top-level by attachment id), per
/// API_CONTRACT §5.2 (Wave 3, ADR-0018). All endpoints are M(team of ticket) and require auth. Upload
/// and delete are team-write; list and download are team-read. The download forces a safe attachment
/// disposition with <c>X-Content-Type-Options: nosniff</c> — never an inline render (§7.1).
/// </summary>
[ApiController]
public sealed class AttachmentsController : ControllerBase
{
    private readonly AttachmentService _attachments;

    public AttachmentsController(AttachmentService attachments) => _attachments = attachments;

    // ----- List a ticket's attachments (§5.2) — team-read -----
    [HttpGet("api/tickets/{ticketId:guid}/attachments")]
    public async Task<ActionResult<IReadOnlyList<AttachmentDto>>> List(Guid ticketId, CancellationToken ct)
        => Ok(await _attachments.ListAsync(ticketId, ct));

    // ----- Upload a file to a ticket (§5.2) — team-write, multipart, streamed -----
    // The size cap is enforced in the service while streaming (413). The request-size ceiling here is a
    // generous outer guard (nginx's client_max_body_size is the true edge guard, §9.1); it is sized above
    // the app cap so a slightly-over file still reaches the service and returns a clean 413, not a raw 400.
    [HttpPost("api/tickets/{ticketId:guid}/attachments")]
    [RequestSizeLimit(64L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 64L * 1024 * 1024)]
    public async Task<ActionResult<AttachmentDto>> Upload(Guid ticketId, CancellationToken ct)
    {
        if (!Request.HasFormContentType)
            throw ServiceException.Validation("file", "Expected a multipart/form-data upload.");

        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);
        if (file is null || file.Length == 0)
            throw ServiceException.Validation("file", "A file is required.");

        await using var stream = file.OpenReadStream();
        var upload = new AttachmentUpload(stream, file.FileName, file.ContentType);
        var dto = await _attachments.UploadAsync(ticketId, upload, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    // ----- Download the blob (§5.2) — team-read, forced attachment, nosniff, never inline -----
    [HttpGet("api/attachments/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var download = await _attachments.OpenForDownloadAsync(id, ct);

        // Forced-download disposition with a sanitized filename (RFC 6266 quoting handled by the helper).
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(download.FileName);
        Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        // The browser must NOT re-interpret the body (defeats content-sniffing XSS, §7.1).
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "private";

        return File(download.Content, download.ContentType);
    }

    // ----- Delete (§5.2) — team-write ([ASSUMPTION W3-ATT-DELETE]) -----
    [HttpDelete("api/attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _attachments.DeleteAsync(id, ct);
        return NoContent();
    }
}
