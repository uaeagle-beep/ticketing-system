// QA acceptance: en/uk translation-bundle parity + no-stub check (Wave 3 i18n, ADR-0022; test-guidance
// §11 F). This wave shipped stub bundles at one point, so this guards that:
//   (1) every namespace exists in BOTH languages;
//   (2) every en key has a uk counterpart (no missing Ukrainian translation);
//   (3) the ONLY uk-only keys are the Slavic plural categories `_few`/`_many` (Ukrainian has 4 plural
//       forms one/few/many/other; English has only one/other — so those uk-only keys are correct, not
//       missing English);
//   (4) no uk value is an empty/whitespace stub.
// It reads the JSON bundles directly (not through i18next) so a parity gap can't hide behind fallbackLng.

import { describe, expect, it } from 'vitest';

// Vite/vitest can import every bundle eagerly for a static parity sweep.
const enModules = import.meta.glob('../locales/en/*.json', { eager: true }) as Record<
  string,
  { default: Record<string, unknown> }
>;
const ukModules = import.meta.glob('../locales/uk/*.json', { eager: true }) as Record<
  string,
  { default: Record<string, unknown> }
>;

function nsName(path: string): string {
  return path.split('/').pop()!.replace('.json', '');
}

function byNamespace(mods: typeof enModules): Record<string, Record<string, unknown>> {
  const out: Record<string, Record<string, unknown>> = {};
  for (const [p, m] of Object.entries(mods)) out[nsName(p)] = m.default;
  return out;
}

function flatten(obj: Record<string, unknown>, prefix = ''): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      Object.assign(out, flatten(v as Record<string, unknown>, key));
    } else {
      out[key] = v;
    }
  }
  return out;
}

const en = byNamespace(enModules);
const uk = byNamespace(ukModules);

// The Slavic plural categories legitimately present in uk but not en (English has only _one/_other).
const SLAVIC_PLURAL_ONLY = /_(few|many)$/;

describe('i18n bundle parity (en ↔ uk)', () => {
  it('exposes the same set of namespaces in both languages', () => {
    expect(Object.keys(uk).sort()).toEqual(Object.keys(en).sort());
    expect(Object.keys(en).length).toBeGreaterThan(0);
  });

  it('every English key has a Ukrainian counterpart (no missing translation)', () => {
    const missing: string[] = [];
    for (const ns of Object.keys(en)) {
      const enKeys = Object.keys(flatten(en[ns]));
      const ukKeys = new Set(Object.keys(flatten(uk[ns])));
      for (const k of enKeys) if (!ukKeys.has(k)) missing.push(`${ns}:${k}`);
    }
    expect(missing, `keys present in en but missing in uk:\n${missing.join('\n')}`).toEqual([]);
  });

  it('the only uk-only keys are the Slavic _few/_many plural forms', () => {
    const unexpected: string[] = [];
    for (const ns of Object.keys(uk)) {
      const enKeys = new Set(Object.keys(flatten(en[ns])));
      const ukKeys = Object.keys(flatten(uk[ns]));
      for (const k of ukKeys) {
        if (!enKeys.has(k) && !SLAVIC_PLURAL_ONLY.test(k)) unexpected.push(`${ns}:${k}`);
      }
    }
    expect(
      unexpected,
      `uk keys with no en counterpart that are NOT _few/_many plural forms:\n${unexpected.join('\n')}`,
    ).toEqual([]);
  });

  it('has no empty/whitespace Ukrainian stub values', () => {
    const stubs: string[] = [];
    for (const ns of Object.keys(uk)) {
      for (const [k, v] of Object.entries(flatten(uk[ns]))) {
        if (typeof v === 'string' && v.trim() === '') stubs.push(`${ns}:${k}`);
      }
    }
    expect(stubs, `empty uk values (stubs):\n${stubs.join('\n')}`).toEqual([]);
  });

  it('has no empty/whitespace English stub values', () => {
    const stubs: string[] = [];
    for (const ns of Object.keys(en)) {
      for (const [k, v] of Object.entries(flatten(en[ns]))) {
        if (typeof v === 'string' && v.trim() === '') stubs.push(`${ns}:${k}`);
      }
    }
    expect(stubs, `empty en values (stubs):\n${stubs.join('\n')}`).toEqual([]);
  });
});
