// i18n config unit tests (Wave 3, ADR-0022): language resolution order, localStorage persistence,
// and profile-locale bootstrap precedence. These exercise the SAME singleton the app + tests use
// (initialized to 'en' by the global setup); each test switches/reads explicitly and the global
// afterEach resets the singleton back to 'en'.

import { describe, expect, it, beforeEach } from 'vitest';
import i18n, {
  LANGUAGE_STORAGE_KEY,
  readStoredLanguage,
  storeLanguage,
  setLanguage,
  currentLanguage,
  applyProfileLocale,
} from './config';

describe('i18n config', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('is initialized (pinned to en by the test setup)', () => {
    expect(i18n.isInitialized).toBe(true);
    expect(currentLanguage()).toBe('en');
  });

  it('readStoredLanguage returns only supported codes, else null', () => {
    expect(readStoredLanguage()).toBeNull();
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'uk');
    expect(readStoredLanguage()).toBe('uk');
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr');
    expect(readStoredLanguage()).toBeNull();
  });

  it('setLanguage switches the active language AND persists it to localStorage', async () => {
    await setLanguage('uk');
    expect(currentLanguage()).toBe('uk');
    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('uk');
  });

  it('storeLanguage writes the choice to localStorage', () => {
    storeLanguage('en');
    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
  });

  describe('applyProfileLocale (resolution order step 2)', () => {
    it('applies a supported profile locale when there is NO local choice', () => {
      applyProfileLocale('uk');
      expect(currentLanguage()).toBe('uk');
      expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('uk');
    });

    it('does NOT override an explicit localStorage choice (localStorage wins)', async () => {
      await setLanguage('en'); // explicit local choice
      applyProfileLocale('uk'); // profile says uk, but local choice wins
      expect(currentLanguage()).toBe('en');
    });

    it('ignores a null / unsupported profile locale (stays on current language)', () => {
      applyProfileLocale(null);
      expect(currentLanguage()).toBe('en');
      applyProfileLocale('fr');
      expect(currentLanguage()).toBe('en');
    });
  });
});
