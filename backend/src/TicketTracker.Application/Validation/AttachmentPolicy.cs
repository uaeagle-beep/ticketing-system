using System.Text;

namespace TicketTracker.Application.Validation;

/// <summary>
/// The attachment content-type allowlist + magic-byte sniffer (Wave 3, ADR-0018 §Decision, §7.1).
/// The service is the authoritative source of truth for allowed types (like WIP bounds / label color):
/// a declared type NOT on the allowlist, or a declared type whose magic bytes contradict the payload,
/// is rejected → <c>415 unsupported_media_type</c>. Combined with opaque storage keys, forced
/// <c>Content-Disposition: attachment</c> and <c>X-Content-Type-Options: nosniff</c> on download, this
/// defeats stored-XSS / drive-by-exec via a spoofed upload (e.g. an <c>.exe</c> or HTML sent as
/// <c>image/png</c>). Explicitly denies <c>text/html</c>, <c>image/svg+xml</c> and executables (they are
/// simply absent from the allowlist).
/// </summary>
public static class AttachmentPolicy
{
    /// <summary>
    /// The allowed content-types (declared value must match one of these, case-insensitively). Images,
    /// PDF, plain text, CSV, zip, and the common office document types ([ASSUMPTION W3-ATT-LIMITS]).
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "application/pdf",
        "text/plain",
        "text/csv",
        "application/zip",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>True when <paramref name="contentType"/> (the bare media type, no parameters) is on the allowlist.</summary>
    public static bool IsAllowed(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType) && AllowedContentTypes.Contains(NormalizeMediaType(contentType));

    /// <summary>
    /// Strip any <c>; charset=…</c> / <c>; boundary=…</c> parameters and lowercase-trim the bare media type.
    /// </summary>
    public static string NormalizeMediaType(string contentType)
    {
        var semi = contentType.IndexOf(';');
        var bare = semi >= 0 ? contentType[..semi] : contentType;
        return bare.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Sniff the leading bytes and confirm they are CONSISTENT with the declared (allowlisted) type. Returns
    /// false when the payload's magic bytes contradict the declared type (spoof) OR when the payload looks
    /// like a denied/dangerous format (HTML, SVG, MZ/ELF executable, shebang script) regardless of the
    /// declared type. For the text types (which have no reliable magic) we additionally reject anything that
    /// sniffs as one of the dangerous binary/markup formats.
    /// <para>
    /// <paramref name="head"/> should be the first bytes of the stream (a small prefix, e.g. 512 bytes).
    /// </para>
    /// </summary>
    public static bool SniffMatches(string declaredContentType, ReadOnlySpan<byte> head)
    {
        var type = NormalizeMediaType(declaredContentType);

        // A payload that sniffs as a dangerous format is rejected no matter what type was declared.
        if (LooksDangerous(head))
            return false;

        return type switch
        {
            "image/png" => StartsWith(head, PngMagic),
            "image/jpeg" => StartsWith(head, JpegMagic),
            "image/gif" => StartsWith(head, Gif87Magic) || StartsWith(head, Gif89Magic),
            "image/webp" => IsWebp(head),
            "application/pdf" => StartsWith(head, PdfMagic),
            // ZIP container: plain zip AND the OOXML office docs (docx/xlsx) are ZIP archives.
            "application/zip" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                => IsZip(head),
            // Legacy office (doc/xls) are OLE Compound File Binary Format.
            "application/msword" or
            "application/vnd.ms-excel"
                => StartsWith(head, OleMagic),
            // text/plain, text/csv: no reliable magic. Accept as long as it isn't a dangerous format
            // (already excluded above) and contains no NUL byte in the head (a NUL strongly implies binary,
            // not the declared text). This keeps genuine UTF-8/ASCII/UTF-16 text through while blocking
            // binaries mislabeled as text.
            "text/plain" or "text/csv" => !head.Contains((byte)0),
            _ => false,
        };
    }

    // ----- magic byte signatures -----

    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Gif87Magic = Encoding.ASCII.GetBytes("GIF87a");
    private static readonly byte[] Gif89Magic = Encoding.ASCII.GetBytes("GIF89a");
    private static readonly byte[] PdfMagic = Encoding.ASCII.GetBytes("%PDF-");
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };       // "PK\x03\x04"
    private static readonly byte[] ZipEmptyMagic = { 0x50, 0x4B, 0x05, 0x06 };  // empty archive
    private static readonly byte[] OleMagic = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] RiffMagic = Encoding.ASCII.GetBytes("RIFF");
    private static readonly byte[] WebpMagic = Encoding.ASCII.GetBytes("WEBP");
    private static readonly byte[] MzMagic = { 0x4D, 0x5A };                    // "MZ" — DOS/PE executable
    private static readonly byte[] ElfMagic = { 0x7F, 0x45, 0x4C, 0x46 };       // "\x7FELF"
    private static readonly byte[] ShebangMagic = { 0x23, 0x21 };               // "#!"

    private static bool IsZip(ReadOnlySpan<byte> head)
        => StartsWith(head, ZipMagic) || StartsWith(head, ZipEmptyMagic);

    private static bool IsWebp(ReadOnlySpan<byte> head)
        => head.Length >= 12 && StartsWith(head, RiffMagic) && StartsWith(head[8..], WebpMagic);

    /// <summary>
    /// True when the head looks like a format we must never accept regardless of the declared type:
    /// an MZ/ELF executable, a shebang script, or an HTML document (case-insensitive leading
    /// <c>&lt;!doctype html</c> / <c>&lt;html</c> / <c>&lt;script</c>, or an SVG root).
    /// </summary>
    private static bool LooksDangerous(ReadOnlySpan<byte> head)
    {
        if (StartsWith(head, MzMagic) || StartsWith(head, ElfMagic) || StartsWith(head, ShebangMagic))
            return true;

        // Sniff HTML/SVG in the leading text (skip a UTF-8 BOM and leading ASCII whitespace).
        var text = LeadingAsciiLower(head, 256);
        if (text.Length == 0)
            return false;

        var trimmed = text.TrimStart();
        return trimmed.StartsWith("<!doctype html", StringComparison.Ordinal)
            || trimmed.StartsWith("<html", StringComparison.Ordinal)
            || trimmed.StartsWith("<script", StringComparison.Ordinal)
            || trimmed.StartsWith("<svg", StringComparison.Ordinal)
            || trimmed.StartsWith("<?xml", StringComparison.Ordinal) && trimmed.Contains("<svg", StringComparison.Ordinal);
    }

    private static string LeadingAsciiLower(ReadOnlySpan<byte> head, int max)
    {
        var start = 0;
        // Skip a UTF-8 BOM if present.
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            start = 3;

        var count = Math.Min(head.Length - start, max);
        if (count <= 0)
            return string.Empty;

        var sb = new StringBuilder(count);
        for (var i = start; i < start + count; i++)
        {
            var ch = head[i];
            if (ch == 0)
                break; // binary — stop sniffing as text
            sb.Append(char.ToLowerInvariant((char)ch));
        }
        return sb.ToString();
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix)
        => data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
