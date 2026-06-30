import { describe, expect, it } from 'vitest';
import { relativeTime, formatUtc } from './time';

// All timestamps are ISO-8601 UTC with trailing Z (API_CONTRACT §9). We pin
// `now` explicitly so the relative buckets are deterministic regardless of when
// the suite runs.
const NOW = new Date('2026-06-30T12:00:00Z');

describe('relativeTime', () => {
  it('returns "just now" for very recent times (< 45s)', () => {
    expect(relativeTime('2026-06-30T11:59:30Z', NOW)).toBe('just now');
    expect(relativeTime('2026-06-30T12:00:00Z', NOW)).toBe('just now');
  });

  it('returns "just now" for future timestamps (negative delta)', () => {
    expect(relativeTime('2026-06-30T12:05:00Z', NOW)).toBe('just now');
  });

  it('formats minutes', () => {
    expect(relativeTime('2026-06-30T11:55:00Z', NOW)).toBe('5m ago');
  });

  it('formats hours', () => {
    expect(relativeTime('2026-06-30T09:00:00Z', NOW)).toBe('3h ago');
  });

  it('formats days', () => {
    expect(relativeTime('2026-06-28T12:00:00Z', NOW)).toBe('2d ago');
  });

  it('formats weeks', () => {
    expect(relativeTime('2026-06-16T12:00:00Z', NOW)).toBe('2w ago');
  });

  it('falls back to an absolute date for items older than ~5 weeks', () => {
    // ~3 months back: must NOT be a relative bucket; should contain a year.
    const out = relativeTime('2026-03-01T12:00:00Z', NOW);
    expect(out).not.toMatch(/ago$/);
    expect(out).toMatch(/2026/);
  });

  it('returns an empty string for an unparseable timestamp', () => {
    expect(relativeTime('not-a-date', NOW)).toBe('');
  });
});

describe('formatUtc', () => {
  it('formats an absolute UTC timestamp as "Mon D, HH:MM UTC"', () => {
    expect(formatUtc('2026-06-22T09:15:00Z')).toBe('Jun 22, 09:15 UTC');
  });

  it('zero-pads hours and minutes', () => {
    expect(formatUtc('2026-01-05T03:07:00Z')).toBe('Jan 5, 03:07 UTC');
  });

  it('uses UTC, not local time (no off-by-timezone shift)', () => {
    // Late-UTC time would roll to the next local day in many zones; assert it
    // stays on the UTC day/hour.
    expect(formatUtc('2026-06-22T23:30:00Z')).toBe('Jun 22, 23:30 UTC');
  });

  it('returns the raw input when it is not a valid date', () => {
    expect(formatUtc('garbage')).toBe('garbage');
  });
});
