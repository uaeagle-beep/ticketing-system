// Playwright E2E configuration.
//
// These tests run against the FULL Docker stack (web + api + db + mailpit),
// NOT against `vite dev`. The stack is brought up out-of-band with:
//
//   docker compose -f docker-compose.yml -f docker-compose.e2e.yml up --build -d
//
// so there is intentionally NO `webServer` block here — Playwright only drives a
// browser against an already-running app. The base URL defaults to the single
// browser entry point (http://localhost:8080, ADR-0005) and can be overridden
// with E2E_BASE_URL. The Mailpit REST API base (for reading the verification
// email) defaults to http://localhost:8025 and is overridable with
// MAILPIT_BASE_URL (see e2e/helpers/mailpit.ts).

import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:8080';

export default defineConfig({
  testDir: './e2e',
  // The happy path is a sequential signup -> verify -> login -> ... flow that
  // depends on email round-tripping through Mailpit; keep workers serialized so
  // separate spec files don't race on the shared Mailpit inbox / DB state.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  // Generous timeout: the happy path drives several network round-trips and a
  // drag-and-drop with a reload.
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [['list'], ['html', { open: 'never' }]]
    : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
