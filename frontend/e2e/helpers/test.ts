// Shared Playwright `test` for all E2E specs.
//
// Wave 3 added i18n (ADR-0022) and the SPA now DEFAULTS to Ukrainian (`uk`) — resolution order is
// localStorage(`tt.lang`) -> profile locale -> `uk`. The E2E specs assert English labels/text (the
// same way the unit suite is pinned to `en`), so we pin the browser UI to English here by seeding the
// language localStorage key BEFORE any app script runs, on every navigation and in every context.
//
// `page.addInitScript` runs before the page's own scripts, so `initI18n()` (config.ts) reads 'en' from
// localStorage instead of falling back to 'uk'. This is origin-agnostic (works on :8080 / :8090 / prod),
// unlike a storageState origin match. Specs import { test, expect } from './helpers/test' instead of
// directly from '@playwright/test'.

import { test as base } from '@playwright/test';

export const test = base.extend({
  page: async ({ page }, use) => {
    await page.addInitScript(() => {
      try {
        window.localStorage.setItem('tt.lang', 'en');
      } catch {
        /* private-mode / disabled storage: the app still falls back, but this should not throw */
      }
    });
    await use(page);
  },
});

export { expect } from '@playwright/test';
