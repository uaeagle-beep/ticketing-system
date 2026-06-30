# ADR 0004 — Email delivery: IEmailSender abstraction with MailKit/SMTP in production and a fake in tests

- **Status:** Accepted
- **Date:** 2026-06-30
- **Deciders:** Architect
- **Source refs:** REQUIREMENTS_SOURCE §3 (SMTP, relay1.dataart.com), §11; ANALYSIS FR-E1-3, FR-E8-3, NFR-SEC-4, A30
- **Related ADRs:** 0002 (test DB)

## Context

Sign-up and resend send a verification email through a **configurable SMTP service that must support `relay1.dataart.com`** (§3), with no committed credentials (§11, A30). Tests must run with **no real SMTP** and must be able to assert that an email "would have been sent" and to read the verification token/link it carried (so an API test can complete the verify flow end-to-end without a mailbox).

### Options considered

1. **Call MailKit directly from the auth service.** Couples business logic to transport; untestable without a network SMTP server.
2. **Define an `IEmailSender` port; inject an implementation (chosen).** Production binds `SmtpEmailSender` (MailKit). Tests bind `FakeEmailSender` that records sent messages in memory. The chosen verification-link base URL is injected so the link in the email is environment-correct (A30).

## Decision

Define in the Application layer:

```csharp
public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string verificationLink, CancellationToken ct);
}
```

- **Production:** `SmtpEmailSender` uses MailKit (`SmtpClient`) configured from env: `SMTP_HOST` (default `relay1.dataart.com`), `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD` (optional — relay may be unauthenticated), `SMTP_USE_STARTTLS`, `EMAIL_FROM`. The verification link is built as `${FRONTEND_URL}/verify-email?token=<token>`. The raw token appears only in this link (the one allowed token-in-URL case, §9).
- **Resilience:** SMTP send is awaited inside the request for simplicity at hackathon scale, but wrapped so that a transient SMTP failure does NOT roll back account creation — the account is persisted, and the user can use **resend** if the first mail never arrives (matches the resend story US-AUTH-3). Send failures are logged (never logging credentials or the token).
- **Tests:** `FakeEmailSender` implements the same interface, appends each `(toEmail, verificationLink)` to a thread-safe list exposed to the test. The `WebApplicationFactory` replaces the `IEmailSender` registration with a singleton `FakeEmailSender`, letting an API-flow test sign up, pull the link the fake "sent", extract the `token` query param, and call `POST /api/auth/verify-email` — a complete no-Docker, no-SMTP flow.
- A `MAILDEV`/console option is documented for local manual QA (set `SMTP_HOST` to a local catcher), but the default compose uses the configured relay via `.env`.

## Consequences

- **Positive:** Business logic depends only on the port; SMTP transport is swappable and fully isolated in tests; the verify flow is testable end-to-end offline; no secrets in code (all SMTP config via env).
- **Negative:** In-request synchronous send adds latency to sign-up/resend (acceptable at scale; could move to a background queue later — explicitly out of hackathon scope).
