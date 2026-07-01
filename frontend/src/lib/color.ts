// Color helpers for label chips (Wave 2, ADR-0016). A label carries a "#rrggbb" background color; the
// chip's text color is picked for legibility against that background (WCAG 1.4.1 — the label text, not
// color, conveys the value, but the text must still be readable). We compute relative luminance and pick
// black or white, whichever has the higher contrast — the standard, dependency-free approach.

/** Parse "#rrggbb" (or "#rgb") into [r, g, b] 0-255, or null if it isn't a valid hex color. */
function parseHex(hex: string): [number, number, number] | null {
  const value = hex.trim().replace(/^#/, '');
  if (value.length === 3) {
    const c0 = value.slice(0, 1);
    const c1 = value.slice(1, 2);
    const c2 = value.slice(2, 3);
    const r = parseInt(c0 + c0, 16);
    const g = parseInt(c1 + c1, 16);
    const b = parseInt(c2 + c2, 16);
    return Number.isNaN(r + g + b) ? null : [r, g, b];
  }
  if (value.length === 6) {
    const r = parseInt(value.slice(0, 2), 16);
    const g = parseInt(value.slice(2, 4), 16);
    const b = parseInt(value.slice(4, 6), 16);
    return Number.isNaN(r + g + b) ? null : [r, g, b];
  }
  return null;
}

/** Relative luminance per WCAG 2.x (sRGB), 0 (black) to 1 (white). */
function relativeLuminance([r, g, b]: [number, number, number]): number {
  const channel = (c: number) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Choose a readable text color (black or white) for a given "#rrggbb" background. Falls back to black
 * for an unparseable color. The 0.5 luminance threshold gives the better of the two contrasts across
 * the whole hue range.
 */
export function readableTextColor(backgroundHex: string): string {
  const rgb = parseHex(backgroundHex);
  if (!rgb) return '#000000';
  return relativeLuminance(rgb) > 0.5 ? '#111827' : '#ffffff';
}
