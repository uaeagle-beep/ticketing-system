// Drift guard: the Help page renders these bundled copies because the frontend Docker build context
// is ./frontend only and cannot import from ../docs. The canonical source of truth stays in docs/;
// this test fails if the bundled copy drifts from it, so an edit to the guide must update both.
// (Line endings are normalized so a CRLF/LF checkout difference doesn't cause a false failure.)

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import bundledUk from './user-guide.uk.md?raw';
import bundledEn from './user-guide.en.md?raw';

const here = path.dirname(fileURLToPath(import.meta.url));
const docsDir = path.resolve(here, '../../../docs');
const normalize = (s: string) => s.replace(/\r\n/g, '\n');

describe('user guide bundled copies are in sync with docs/', () => {
  it('user-guide.uk.md matches docs/USER_GUIDE.md', () => {
    const canonical = readFileSync(path.join(docsDir, 'USER_GUIDE.md'), 'utf8');
    expect(normalize(bundledUk)).toEqual(normalize(canonical));
  });

  it('user-guide.en.md matches docs/USER_GUIDE.en.md', () => {
    const canonical = readFileSync(path.join(docsDir, 'USER_GUIDE.en.md'), 'utf8');
    expect(normalize(bundledEn)).toEqual(normalize(canonical));
  });
});
