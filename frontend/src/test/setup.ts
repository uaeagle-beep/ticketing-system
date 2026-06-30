// Global test setup (Vitest + jsdom). Loaded via vite.config.ts `test.setupFiles`.
//
// Responsibilities:
//  - Register @testing-library/jest-dom matchers (toBeInTheDocument, etc.).
//  - Start the MSW server before all tests; reset request handlers after each
//    test so per-test `server.use(...)` overrides don't leak; stop after all.
//  - Clean up the React tree and reset auth-token / localStorage state between
//    tests so suites stay isolated.

import '@testing-library/jest-dom/vitest';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { cleanup } from '@testing-library/react';
import { server } from './server';
import { setToken } from '@/api/tokenStore';

// jsdom performs no layout, so HTMLElement.offsetParent is always null. Several
// components (notably ConfirmDialog's focus trap) treat `offsetParent === null`
// as "not visible" and would therefore see zero focusable elements. Polyfill it
// to return a sensible parent for connected, non-hidden elements so visibility
// checks behave like a real browser.
// NOTE: jsdom already defines an `offsetParent` getter that ALWAYS returns null,
// so a "define only if missing" guard would never install this. Override it
// unconditionally, otherwise the visibility check sees zero focusable elements
// and the focus trap can't move focus (ConfirmDialog focus-trap tests).
Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
  configurable: true,
  get() {
    // Hidden elements (display:none) and detached elements have no offsetParent.
    let node: HTMLElement | null = this as HTMLElement;
    while (node) {
      if (node.style && node.style.display === 'none') return null;
      node = node.parentElement;
    }
    return (this as HTMLElement).parentElement ?? null;
  },
});

// `onUnhandledRequest: 'error'` surfaces any /api call we forgot to mock as a
// test failure instead of a silent network hang — keeps handlers honest.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

afterEach(() => {
  cleanup();
  server.resetHandlers();
  // Drop any token set during the test (the store mirrors to localStorage and
  // keeps an in-memory copy that would otherwise bleed into the next test).
  setToken(null);
  try {
    window.localStorage.clear();
  } catch {
    /* jsdom always provides localStorage; ignore if unavailable */
  }
});

afterAll(() => server.close());
