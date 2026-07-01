// i18n translation-behavior tests (Wave 3, ADR-0022): switching to Ukrainian flips visible strings
// through the shared lib helpers (errors/labels/time), and error CODES map to localized messages via
// the `errors` namespace. English output is verified elsewhere (the 279 en-pinned suite); here we
// prove the uk side and the code→message mapping. The global afterEach resets the singleton to 'en'.

import { describe, expect, it, afterEach } from 'vitest';
import i18n from './config';
import { errorMessage } from '@/lib/errors';
import { stateLabel, priorityLabel } from '@/lib/labels';
import { relativeTime } from '@/lib/time';
import { ApiError } from '@/api/client';

async function withUk(fn: () => void) {
  await i18n.changeLanguage('uk');
  fn();
}

describe('i18n translation behavior', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('enum labels are English by default and Ukrainian after switching', async () => {
    expect(stateLabel('in_progress')).toBe('In progress');
    expect(priorityLabel('high')).toBe('High');
    await withUk(() => {
      expect(stateLabel('in_progress')).toBe('У роботі');
      expect(priorityLabel('high')).toBe('Високий');
    });
  });

  it('error CODES map to localized messages (errors namespace)', async () => {
    const err = new ApiError(409, 'duplicate_team_name', 'server msg');
    expect(errorMessage(err)).toBe('A team with this name already exists.');
    await withUk(() => {
      expect(errorMessage(err)).toBe('Команда з такою назвою вже існує.');
    });
  });

  it('a Wave-3 error code (payload_too_large) localizes on both sides', async () => {
    const err = new ApiError(413, 'payload_too_large', 'srv');
    expect(errorMessage(err)).toBe('That file is too large. The maximum size is 10 MB.');
    await withUk(() => {
      expect(errorMessage(err)).toBe('Цей файл завеликий. Максимальний розмір — 10 МБ.');
    });
  });

  it('an unknown code still falls back to the server message (codes stay the contract)', async () => {
    const err = new ApiError(418, 'some_brand_new_code', 'A specific server message.');
    expect(errorMessage(err)).toBe('A specific server message.');
    await withUk(() => {
      expect(errorMessage(err)).toBe('A specific server message.');
    });
  });

  it('relative time is locale-aware (buckets translate)', async () => {
    const now = new Date('2026-06-30T12:00:00Z');
    expect(relativeTime('2026-06-30T11:55:00Z', now)).toBe('5m ago');
    await withUk(() => {
      expect(relativeTime('2026-06-30T11:55:00Z', now)).toBe('5 хв тому');
      expect(relativeTime('2026-06-30T12:00:00Z', now)).toBe('щойно');
    });
  });
});
