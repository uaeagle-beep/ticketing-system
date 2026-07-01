// Admin bootstrap for E2E under the User-Management authorization model.
//
// Team/epic CRUD is admin-only (ADR-0007) and a fresh e2e database has NO admin
// to grant the role through the UI. So, after an account has signed up + verified,
// we flip its `is_admin` flag directly in the e2e stack's Postgres — the same
// "promote in the DB" bootstrap the backend integration tests use. The server
// reads isAdmin fresh from the DB on every request, so a `page.reload()` right
// after this makes the SPA refetch /me and reveal the admin UI (no re-login).
//
// This talks to the DB via `docker compose ... exec db psql`, so it only works
// against the running e2e stack (which the happy-path already requires). The
// compose project is selected by COMPOSE_PROJECT_NAME (inherited from the env),
// so it targets the same stack the CI/`up` step created regardless of cwd.

import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// frontend/e2e/helpers -> repo root (ESM-safe: derive the dir from import.meta.url,
// since this package is "type": "module" and __dirname is not defined).
const HELPER_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HELPER_DIR, '..', '..', '..');
const PG_USER = process.env.POSTGRES_USER ?? 'ticketing';
const PG_DB = process.env.POSTGRES_DB ?? 'ticketing';

/** Promote a signed-up account to administrator by flipping is_admin in the e2e DB. */
export function promoteToAdmin(email: string): void {
  // E2E emails are test-generated (e2e-<runid>@example.com) with no quotes, but
  // normalise defensively; the column stores the trimmed-lowercased key.
  const normalized = email.trim().toLowerCase().replace(/'/g, "''");
  const sql = `UPDATE users SET is_admin = true WHERE email_normalized = '${normalized}';`;

  try {
    execFileSync(
      'docker',
      [
        'compose',
        '-f', path.join(REPO_ROOT, 'docker-compose.yml'),
        '-f', path.join(REPO_ROOT, 'docker-compose.e2e.yml'),
        'exec', '-T', 'db',
        'psql', '-U', PG_USER, '-d', PG_DB, '-v', 'ON_ERROR_STOP=1',
        '-c', sql,
      ],
      { stdio: 'pipe' },
    );
  } catch (err: unknown) {
    const e = err as { stderr?: Buffer; stdout?: Buffer; message?: string };
    const detail = e.stderr?.toString() || e.stdout?.toString() || e.message || String(err);
    throw new Error(`promoteToAdmin(${email}) failed via docker compose exec db psql:\n${detail}`);
  }
}
