// Time formatting helpers. All API timestamps are ISO-8601 UTC with trailing Z.

// Relative "modified time" for board cards (A26), derived from the UTC value.
// Examples: "just now", "5m ago", "3h ago", "2d ago", then an absolute date.
export function relativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const ms = now.getTime() - then.getTime();
  if (Number.isNaN(ms)) return '';

  const sec = Math.round(ms / 1000);
  if (sec < 0) return 'just now';
  if (sec < 45) return 'just now';
  const min = Math.round(sec / 60);
  if (min < 60) return `${min}m ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const day = Math.round(hr / 24);
  if (day < 7) return `${day}d ago`;
  const week = Math.round(day / 7);
  if (week < 5) return `${week}w ago`;

  // Fall back to an absolute, locale-formatted UTC date for older items.
  return then.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
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
  const month = date.toLocaleString('en-US', { month: 'short', timeZone: 'UTC' });
  return `${month} ${date.getUTCDate()}, ${y}`;
}

// Absolute UTC timestamp for the ticket-details meta line (Wireframe 3),
// e.g. "Jun 22, 09:15 UTC".
export function formatUtc(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const month = d.toLocaleString('en-US', { month: 'short', timeZone: 'UTC' });
  const day = d.getUTCDate();
  const hh = String(d.getUTCHours()).padStart(2, '0');
  const mm = String(d.getUTCMinutes()).padStart(2, '0');
  return `${month} ${day}, ${hh}:${mm} UTC`;
}
