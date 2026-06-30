import { describe, expect, it } from 'vitest';
import { displayName } from './displayName';

describe('displayName', () => {
  it('returns the name when it is a non-empty string', () => {
    expect(displayName('Ada Lovelace', 'ada@dataart.com')).toBe('Ada Lovelace');
  });

  it('falls back to the email when the name is null', () => {
    expect(displayName(null, 'ada@dataart.com')).toBe('ada@dataart.com');
  });

  it('falls back to the email when the name is undefined', () => {
    expect(displayName(undefined, 'ada@dataart.com')).toBe('ada@dataart.com');
  });

  it('falls back to the email when the name is empty or whitespace-only', () => {
    expect(displayName('', 'ada@dataart.com')).toBe('ada@dataart.com');
    expect(displayName('   ', 'ada@dataart.com')).toBe('ada@dataart.com');
  });

  it('trims surrounding whitespace from the name', () => {
    expect(displayName('  Ada  ', 'ada@dataart.com')).toBe('Ada');
  });
});
