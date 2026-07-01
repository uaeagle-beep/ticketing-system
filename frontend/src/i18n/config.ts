// i18next singleton for the SPA (Wave 3 i18n, ADR-0022).
//
// This module creates and initializes ONE i18next instance used both by React (via
// react-i18next's <I18nextProvider>/useTranslation) AND by the pure helper modules
// lib/errors.ts, lib/time.ts, lib/labels.ts — those call `i18n.t(...)` on this singleton
// directly (they are not React hooks), so their output tracks the active language.
//
// Language resolution order ([ASSUMPTION W3-I18N-DEFAULT], ADR-0022):
//   1. localStorage (the authoritative UI choice, persists offline/instant)
//   2. the user's profile `locale` from /me (applied on bootstrap by the AuthContext)
//   3. fallback 'uk' (the PO/users are Ukrainian)
// (`navigator.language` is intentionally NOT consulted for the default: the product default
// is uk regardless of browser language; a user opts into en explicitly. localStorage → uk.)
//
// TESTS: the existing 279-test suite asserts on ENGLISH strings. `initI18nForTest()` initializes
// this SAME singleton synchronously pinned to `lng: 'en'` so those assertions keep passing without
// mass rewrites (see src/test/setup.ts). App code uses `initI18n()` (default uk).

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import {
  DEFAULT_NS,
  FALLBACK_LANGUAGE,
  NAMESPACES,
  resources,
  isSupportedLanguage,
} from './resources';

// localStorage key for the persisted UI language choice.
export const LANGUAGE_STORAGE_KEY = 'tt.lang';

/** Read the persisted UI language from localStorage, if it is one we support. */
export function readStoredLanguage(): string | null {
  try {
    const stored = window.localStorage.getItem(LANGUAGE_STORAGE_KEY);
    return isSupportedLanguage(stored) ? stored : null;
  } catch {
    return null;
  }
}

/** Persist the UI language choice to localStorage (authoritative for the UI). */
export function storeLanguage(lang: string): void {
  try {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, lang);
  } catch {
    /* private-mode / disabled storage: ignore, the in-memory choice still applies */
  }
}

// Shared init options — same resources/namespaces regardless of language.
function baseOptions(lng: string) {
  return {
    resources,
    lng,
    fallbackLng: FALLBACK_LANGUAGE,
    supportedLngs: ['uk', 'en'],
    ns: [...NAMESPACES],
    defaultNS: DEFAULT_NS,
    interpolation: {
      // React already escapes values, so i18next must not double-escape (react-i18next guidance).
      escapeValue: false,
    },
    // Deterministic + synchronous: resources are bundled, so there is no async backend load.
    initImmediate: false,
    returnNull: false as const,
  };
}

/**
 * Initialize the app i18n singleton. Language = localStorage → (caller applies profile locale
 * later on bootstrap) → 'uk'. Idempotent: safe to import once from main.tsx.
 */
export function initI18n(): typeof i18n {
  if (i18n.isInitialized) return i18n;
  const initial = readStoredLanguage() ?? FALLBACK_LANGUAGE;
  void i18n.use(initReactI18next).init(baseOptions(initial));
  return i18n;
}

/**
 * Initialize the SAME singleton for tests, pinned to English so the existing English-asserting
 * suite passes unchanged. Synchronous (bundled resources + initImmediate:false). Idempotent.
 */
export function initI18nForTest(): typeof i18n {
  if (i18n.isInitialized) {
    // A prior test may have switched languages; force back to en for isolation.
    if (i18n.language !== 'en') void i18n.changeLanguage('en');
    return i18n;
  }
  void i18n.use(initReactI18next).init(baseOptions('en'));
  return i18n;
}

/**
 * Apply the user's persisted profile `locale` (from /me) on bootstrap — resolution order step 2.
 * localStorage is authoritative for the UI, so this ONLY takes effect when the user has NOT made an
 * explicit local choice yet (no stored language). A supported profile locale then becomes the active
 * language (and is persisted so it sticks). A null/unsupported profile locale is ignored (stays on
 * the current language → the 'uk' fallback). Safe no-op before init.
 */
export function applyProfileLocale(profileLocale: string | null | undefined): void {
  if (!i18n.isInitialized) return;
  if (readStoredLanguage()) return; // explicit local choice wins over the profile
  if (!isSupportedLanguage(profileLocale)) return;
  if (i18n.language === profileLocale) return;
  storeLanguage(profileLocale);
  void i18n.changeLanguage(profileLocale);
}

/** The active language, normalized to a supported code (strips region suffixes like en-US). */
export function currentLanguage(): 'uk' | 'en' {
  const lng = i18n.language;
  if (isSupportedLanguage(lng)) return lng;
  return lng?.startsWith('en') ? 'en' : FALLBACK_LANGUAGE;
}

/**
 * Change the active UI language and persist it to localStorage. Does NOT call the profile API —
 * the caller (the switcher) mirrors it to PUT /api/me/profile when the user is logged in so the
 * choice follows them across devices (ADR-0022).
 */
export async function setLanguage(lang: string): Promise<void> {
  storeLanguage(lang);
  await i18n.changeLanguage(lang);
}

export default i18n;
