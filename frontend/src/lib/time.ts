// Time formatting helpers. All API timestamps are ISO-8601 UTC with trailing Z.
//
// Wave 3 i18n (ADR-0022): relative-time buckets and month abbreviations come from the `time`
// namespace of the i18n singleton (src/i18n/config.ts), so they follow the active language.
// The absolute-UTC / due-date formats keep their UTC-correct, timezone-safe construction and
// only localize the month abbreviation + "UTC" suffix (the numbers/order are language-invariant
// here to preserve the stable "Mon D, HH:MM UTC" shape asserted across the app). These are plain
// functions (not hooks) that read i18n at call time.

import i18n from '@/i18n/config';

// Localized 3-letter month abbreviation (index 0 = January). Falls back to the English
// abbreviation via a UTC toLocaleString if the bundle is somehow missing an entry.
function monthAbbr(monthIndex0: number): string {
  const months = i18n.t('time:months', { returnObjects: true }) as unknown;
  if (Array.isArray(months) && typeof months[monthIndex0] === 'string') {
    return months[monthIndex0] as string;
  }
  // Defensive fallback (should not happen — every bundle ships a 12-entry array).
  return new Date(Date.UTC(2000, monthIndex0, 1)).toLocaleString('en-US', {
    month: 'short',
    timeZone: 'UTC',
  });
}

// Relative "modified time" for board cards (A26), derived from the UTC value.
// Examples (en): "just now", "5m ago", "3h ago", "2d ago", then an absolute date.
export function relativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const ms = now.getTime() - then.getTime();
  if (Number.isNaN(ms)) return '';

  const sec = Math.round(ms / 1000);
  if (sec < 0) return i18n.t('time:justNow');
  if (sec < 45) return i18n.t('time:justNow');
  const min = Math.round(sec / 60);
  if (min < 60) return i18n.t('time:minutesAgo', { count: min });
  const hr = Math.round(min / 60);
  if (hr < 24) return i18n.t('time:hoursAgo', { count: hr });
  const day = Math.round(hr / 24);
  if (day < 7) return i18n.t('time:daysAgo', { count: day });
  const week = Math.round(day / 7);
  if (week < 5) return i18n.t('time:weeksAgo', { count: week });

  // Fall back to an absolute, locale-formatted UTC date for older items.
  const y = then.getUTCFullYear();
  if (Number.isNaN(y)) return '';
  return `${monthAbbr(then.getUTCMonth())} ${then.getUTCDate()}, ${y}`;
}

// Format a calendar-day due date ("YYYY-MM-DD", no time-of-day, F-08) for display,
// e.g. "Jul 5, 2026". Parsed as a plain date (no timezone shift). Invalid input is
// returned unchanged so the raw value is never hidden.
export function formatDueDate(dueDate: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dueDate);
  if (!m) return dueDate;
  const [, y, mo, d] = m;
  const date = new Date(Date.UTC(Number(y), Number(mo) - 1, Number(d)));
  if (Number.isNaN(date.getTime())) return dueDate;
  return `${monthAbbr(date.getUTCMonth())} ${date.getUTCDate()}, ${y}`;
}

// Absolute UTC timestamp for the ticket-details meta line (Wireframe 3),
// e.g. "Jun 22, 09:15 UTC".
export function formatUtc(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const day = d.getUTCDate();
  const hh = String(d.getUTCHours()).padStart(2, '0');
  const mm = String(d.getUTCMinutes()).padStart(2, '0');
  return `${monthAbbr(d.getUTCMonth())} ${day}, ${hh}:${mm} ${i18n.t('time:utcSuffix')}`;
}
