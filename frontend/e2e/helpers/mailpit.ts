// Mailpit REST helper for E2E.
//
// In the E2E stack the `api` service is reconfigured (docker-compose.e2e.yml) to
// send SMTP to the `mailpit` service instead of relay1.dataart.com, so the
// verification email is captured by Mailpit. Mailpit exposes a web UI + REST API
// on port 8025; we poll its message list, find the latest message for a given
// recipient, fetch the message source, and extract the verification link.
//
// The backend builds the link as `${FRONTEND_URL}/verify-email?token=<encoded>`
// (AuthService.BuildVerificationLink) and embeds it in BOTH the text and HTML
// bodies (SmtpEmailSender). We therefore match the `/verify-email?token=...`
// path anywhere in the raw message and return the absolute URL.
//
// Mailpit REST endpoints used (v1 API):
//   GET /api/v1/messages           -> { messages: [{ ID, To: [{Address}], Created }] }
//   GET /api/v1/message/{ID}       -> { Text, HTML, ... } (parsed message)
// Docs: https://mailpit.axllent.org/docs/api-v1/

import { expect, type APIRequestContext } from '@playwright/test';

const MAILPIT_BASE_URL = process.env.MAILPIT_BASE_URL ?? 'http://localhost:8025';

interface MailpitMessageSummary {
  ID: string;
  Created: string;
  To: Array<{ Address: string; Name?: string }>;
  Subject?: string;
}

interface MailpitListResponse {
  messages: MailpitMessageSummary[];
  total?: number;
}

interface MailpitMessageDetail {
  ID: string;
  Text?: string;
  HTML?: string;
}

/** Remove every message from the Mailpit inbox so a poll can't match a stale email. */
export async function clearMailpit(request: APIRequestContext): Promise<void> {
  const res = await request.delete(`${MAILPIT_BASE_URL}/api/v1/messages`);
  expect(
    res.ok(),
    `Failed to clear Mailpit inbox (HTTP ${res.status()}). Is the mailpit service up?`,
  ).toBeTruthy();
}

/**
 * Poll the Mailpit inbox until a message addressed to `recipient` arrives, then
 * return its most-recent verification link. Throws (via expect) if none arrives
 * within `timeoutMs`.
 */
export async function waitForVerificationLink(
  request: APIRequestContext,
  recipient: string,
  timeoutMs = 30_000,
): Promise<string> {
  const target = recipient.trim().toLowerCase();
  const deadline = Date.now() + timeoutMs;
  let lastError = 'no message seen';

  while (Date.now() < deadline) {
    const listRes = await request.get(
      // Newest first; a small page is plenty for a single-recipient E2E run.
      `${MAILPIT_BASE_URL}/api/v1/messages?limit=50`,
    );
    if (listRes.ok()) {
      const list = (await listRes.json()) as MailpitListResponse;
      const match = (list.messages ?? []).find((m) =>
        (m.To ?? []).some((t) => t.Address?.trim().toLowerCase() === target),
      );
      if (match) {
        const detailRes = await request.get(
          `${MAILPIT_BASE_URL}/api/v1/message/${match.ID}`,
        );
        if (detailRes.ok()) {
          const detail = (await detailRes.json()) as MailpitMessageDetail;
          const link = extractVerificationLink(
            `${detail.HTML ?? ''}\n${detail.Text ?? ''}`,
          );
          if (link) return link;
          lastError = `message ${match.ID} for ${target} had no verify-email link`;
        } else {
          lastError = `GET message ${match.ID} failed: HTTP ${detailRes.status()}`;
        }
      } else {
        lastError = `no message for ${target} yet (inbox size ${list.messages?.length ?? 0})`;
      }
    } else {
      lastError = `GET /messages failed: HTTP ${listRes.status()}`;
    }

    await sleep(750);
  }

  throw new Error(
    `Timed out after ${timeoutMs}ms waiting for a verification email to ${recipient}. Last: ${lastError}`,
  );
}

/**
 * Pull the first `${...}/verify-email?token=<token>` URL out of a raw email
 * body. Handles both the HTML anchor (href="...") and the plain-text variant,
 * and decodes HTML entities (&amp;) that a mail body may contain.
 */
export function extractVerificationLink(body: string): string | null {
  // Match an absolute http(s) URL whose path is /verify-email?token=...
  // Stop at the first character that cannot be part of a URL / attribute value.
  const match = body.match(/https?:\/\/[^\s"'<>]*\/verify-email\?token=[^\s"'<>]+/i);
  if (!match) return null;
  // Mail bodies sometimes HTML-escape ampersands in query strings.
  return match[0].replace(/&amp;/g, '&');
}

/** Extract just the `token` query-param value from a verification URL. */
export function tokenFromLink(link: string): string {
  const url = new URL(link);
  const token = url.searchParams.get('token');
  if (!token) throw new Error(`Verification link has no token: ${link}`);
  return token;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
