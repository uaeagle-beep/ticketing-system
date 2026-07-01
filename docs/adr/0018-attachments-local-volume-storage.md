# ADR 0018 — Attachments stored on a local filesystem volume with DB-tracked metadata

- **Status:** Accepted
- **Date:** 2026-07-01
- **Deciders:** Architect
- **Source refs:** Wave 3 approved scope (files on tickets); [`WAVE3_DESIGN.md`](../WAVE3_DESIGN.md) §4.2/§5.2/§7.1/§9.1
- **Related ADRs:** 0005 (Docker single-server topology), 0002 (SQLite tests / EnsureCreated), 0012/0013 (event backbone + fan-out), 0007 (team-scoped authz)

## Context

Wave 3 adds file attachments on tickets. The deploy reality ([ADR-0005]) is a **single Linux server**, Docker Compose (db + api + web), with a `pgdata` named volume for durability and **no object storage, no Redis** today. We need durable blob storage, DB-tracked metadata, an authenticated + team-scoped download, and hard protection against the classic file-upload attacks (path traversal, stored-XSS/exec via content sniffing, oversized uploads). Everything must remain testable on in-memory SQLite without touching the real disk ([ADR-0002]).

## Decision

- **Blobs on a local Docker named volume; metadata in the DB.** A new `attachments:/var/lib/tickettracker/attachments` named volume is mounted into the `api` container (survives `down`/restart like `pgdata`). Only metadata (`Attachment` row: `ticket_id`, `uploaded_by`, `original_filename`, `content_type`, `size_bytes`, `storage_key`, `created_at`) lives in Postgres. **No object storage** — it matches the single-server deploy and adds no managed dependency.
- **`IAttachmentStorage` port is the S3-swap seam.** The service depends on `IAttachmentStorage { Task<string> SaveAsync(Stream, ct); Task<Stream> OpenAsync(storageKey, ct); Task DeleteAsync(storageKey, ct); }`. Production binds `LocalFileAttachmentStorage`; tests bind an in-memory implementation. A future move to S3/MinIO is a new binding, no service/contract change.
- **Server-generated opaque `storage_key`.** The on-disk name is a server-generated GUID-derived key (recommend `{yyyy}/{MM}/{guid}`), **never** the client filename. `original_filename` is stored display-only, sanitized, and emitted only in `Content-Disposition`. The final path is `Path.Combine(root, storageKey)` with a post-combine assertion that the resolved path stays under the root (traversal defense in depth).
- **Content-type allowlist + magic-byte sniff.** Uploads are validated against `AttachmentPolicy.AllowedContentTypes` (images png/jpeg/gif/webp, pdf, text/plain, csv, zip, common office docs) **AND** a magic-byte sniff; a declared/sniffed mismatch or a denied type (esp. `text/html`, `image/svg+xml`, executables) → **415 unsupported_media_type**. Max size = `ATTACHMENTS_MAX_BYTES` (default 10 MB), enforced **while streaming** (abort + delete partial) plus nginx `client_max_body_size` at the proxy → oversized → **413 payload_too_large**.
- **Download is authenticated, team-scoped, forced-download.** `GET /api/attachments/{id}` resolves attachment→ticket→team (404-then-403), streams with `Content-Disposition: attachment`, the stored `Content-Type`, and **`X-Content-Type-Options: nosniff`**, never `inline`. No public/presigned URLs.
- **Delete = M(team of ticket)** (team-write scope, not uploader-only — a file is collaborative ticket content, not an authored voice). Row deleted first, then best-effort blob delete (a crash leaves an orphan blob, not a dangling row; a reaper cleans unreferenced keys).
- **Attachments participate in the event backbone.** `attachment_added` → activity + notification (auto-watch the uploader); `attachment_deleted` → activity only. The two codes extend `EventType` and the `event_type` CHECK on `notifications`/`activity_entries` (Phase-1 migration).
- **Antivirus is out of scope** but the hook is fixed: an `IAttachmentScanner` (no-op today) is called after the stream is written and before commit.

## Consequences

- **Positive:** no new infra dependency; durability via a named volume matching `pgdata`; strong upload/download hardening (opaque keys, allowlist+sniff, forced download, nosniff, size caps); fully testable via the in-memory `IAttachmentStorage` (no disk in CI); a clean S3-swap seam and AV hook for later.
- **Negative (accepted):** blobs are not replicated/backed-up beyond the single host's volume — acceptable at this scale; a real deployment should back up the volume alongside `pgdata`. Orphan blobs can accumulate on crash (reaped separately). Horizontal scale-out would require shared storage (the S3 seam is the answer then).
- **Operational:** the volume must be writable by the non-root api uid (Dockerfile `chown` / `user`); `ATTACHMENTS_MAX_BYTES` and the nginx `client_max_body_size` must stay in sync (nginx cap > file cap + multipart overhead).
