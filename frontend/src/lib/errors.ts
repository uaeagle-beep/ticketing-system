// Map API errors to user-facing messages. Falls back to the server-provided
// message, then to a generic message, so every error surface is human-readable
// (NFR-USE-3).
//
// Wave 3 i18n (ADR-0022): the backend keeps returning STABLE machine error CODES;
// localization is a presentation concern. This maps `code → localized message` via the
// `errors` namespace of the i18n singleton (src/i18n/config.ts). Codes remain the contract
// ([ADR-0006] unchanged). A code with no bundle entry falls back to the server message, then
// to the localized generic message. `errorMessage` is a plain function (not a hook) that reads
// i18n at call time, so it tracks the active language.

import i18n from '@/i18n/config';
import { ApiError } from '@/api/client';

export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    // Prefer per-field validation text when present (most specific).
    const fieldText = err.fieldErrorText();
    if (err.code === 'validation_error' && fieldText) return fieldText;
    // Only use a localized message when the code actually has a bundle entry; otherwise
    // fall back to the server-provided message (unknown/new codes), then the generic message.
    if (i18n.exists(`errors:${err.code}`)) return i18n.t(`errors:${err.code}`);
    return err.message ?? i18n.t('errors:generic');
  }
  if (err instanceof Error) return err.message;
  return i18n.t('errors:generic');
}

export function isApiErrorCode(err: unknown, code: string): boolean {
  return err instanceof ApiError && err.code === code;
}
