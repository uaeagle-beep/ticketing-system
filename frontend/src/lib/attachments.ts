// Shared attachment constants + helpers for the client-side pre-check (Wave 3, ADR-0018 / §10.1).
// The SERVER is authoritative (it re-validates type by declared value AND magic-byte sniff, and enforces
// the size cap while streaming → 413/415); these are a friendly UX guard only, mirroring the server
// allowlist ([ASSUMPTION W3-ATT-LIMITS]) so we can reject obviously-wrong files before the upload round-trip.

// Must stay in sync with AttachmentPolicy.AllowedContentTypes on the backend.
export const ALLOWED_CONTENT_TYPES: readonly string[] = [
  'image/png',
  'image/jpeg',
  'image/gif',
  'image/webp',
  'application/pdf',
  'text/plain',
  'text/csv',
  'application/zip',
  'application/msword',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
];

// Must stay in sync with ATTACHMENTS_MAX_BYTES (default 10 MB).
export const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;

// A friendly `accept` attribute for the file input (extensions the allowlist maps to).
export const ATTACHMENT_ACCEPT =
  '.png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.csv,.zip,.doc,.docx,.xls,.xlsx,' +
  ALLOWED_CONTENT_TYPES.join(',');

/** Human-readable byte size (e.g. "20 KB", "1.4 MB"). */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  if (kb < 1024) return `${Math.round(kb)} KB`;
  const mb = kb / 1024;
  return `${mb.toFixed(mb < 10 ? 1 : 0)} MB`;
}

/**
 * Client-side pre-check mirroring the server allowlist + size cap. Returns a user-facing error string
 * when the file should be rejected before upload, or null when it looks acceptable. The browser's
 * reported MIME type can be empty/wrong — an empty type is allowed through (the server sniffs), but a
 * present-and-disallowed type is rejected early for a snappier UX.
 */
export function precheckFile(file: File): string | null {
  if (file.size === 0) return 'That file is empty.';
  if (file.size > MAX_ATTACHMENT_BYTES) {
    return `That file is too large. The maximum size is ${formatBytes(MAX_ATTACHMENT_BYTES)}.`;
  }
  if (file.type && !ALLOWED_CONTENT_TYPES.includes(file.type)) {
    return 'That file type is not allowed. Try an image, PDF, text, CSV, or Office document.';
  }
  return null;
}
