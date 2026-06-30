import { describe, expect, it } from 'vitest';
import {
  stateLabel,
  typeLabel,
  stateOptions,
  typeOptions,
  orderedStates,
} from './labels';
import { TICKET_STATES, TICKET_TYPES } from '@/api/types';

describe('stateLabel', () => {
  it('maps every canonical state to its human label', () => {
    expect(stateLabel('new')).toBe('New');
    expect(stateLabel('ready_for_implementation')).toBe('Ready for implementation');
    expect(stateLabel('in_progress')).toBe('In progress');
    expect(stateLabel('ready_for_acceptance')).toBe('Ready for acceptance');
    expect(stateLabel('done')).toBe('Done');
  });

  it('covers all canonical states (no missing mapping)', () => {
    for (const state of TICKET_STATES) {
      expect(stateLabel(state)).toBeTruthy();
    }
  });
});

describe('typeLabel', () => {
  it('maps every canonical type to its human label', () => {
    expect(typeLabel('bug')).toBe('Bug');
    expect(typeLabel('feature')).toBe('Feature');
    expect(typeLabel('fix')).toBe('Fix');
  });

  it('covers all canonical types', () => {
    for (const type of TICKET_TYPES) {
      expect(typeLabel(type)).toBeTruthy();
    }
  });
});

describe('option lists', () => {
  it('stateOptions preserves canonical workflow order', () => {
    expect(stateOptions.map((o) => o.value)).toEqual([...TICKET_STATES]);
    expect(stateOptions.map((o) => o.label)).toEqual([
      'New',
      'Ready for implementation',
      'In progress',
      'Ready for acceptance',
      'Done',
    ]);
  });

  it('typeOptions pairs each value with its label', () => {
    expect(typeOptions).toEqual([
      { value: 'bug', label: 'Bug' },
      { value: 'feature', label: 'Feature' },
      { value: 'fix', label: 'Fix' },
    ]);
  });

  it('orderedStates equals the canonical five-state workflow order', () => {
    expect(orderedStates).toEqual([...TICKET_STATES]);
    expect(orderedStates).toHaveLength(5);
  });
});
