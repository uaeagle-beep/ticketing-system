import { describe, expect, it, vi } from 'vitest';
import type { ClientRect } from '@dnd-kit/core';
import { boardKeyboardCoordinates } from './keyboardCoordinates';

// Pure-logic test for the keyboard coordinate getter that backs the a11y fix
// A11Y-1: ArrowLeft/Right move the dragged card to the adjacent droppable
// COLUMN; edges do NOT wrap; ArrowUp/Down nudge within the current column.
//
// We mock dnd-kit's sensor `context` snapshot: a horizontal row of five
// measured column rects (one per workflow state), `droppableRects` as a Map,
// and a `collisionRect` placed over a chosen column so the getter can pick the
// "current" column by nearest centre.

const COLUMN_WIDTH = 200;
const COLUMN_TOP = 100;
const COLUMN_HEIGHT = 600;

function rect(left: number): ClientRect {
  return {
    left,
    top: COLUMN_TOP,
    width: COLUMN_WIDTH,
    height: COLUMN_HEIGHT,
    right: left + COLUMN_WIDTH,
    bottom: COLUMN_TOP + COLUMN_HEIGHT,
  };
}

// Five columns side by side: indices 0..4 at x = 0,200,400,600,800.
const COLUMN_IDS = [
  'new',
  'ready_for_implementation',
  'in_progress',
  'ready_for_acceptance',
  'done',
] as const;

const COLUMN_RECTS: ClientRect[] = COLUMN_IDS.map((_, i) => rect(i * COLUMN_WIDTH));

function centerX(r: ClientRect): number {
  return r.left + r.width / 2;
}

// Build the argument object the getter receives. `overColumnIndex` is the
// column the dragged card currently overlaps (drives baseIndex selection).
function makeArgs(overColumnIndex: number) {
  const droppableRects = new Map<string, ClientRect>(
    COLUMN_IDS.map((id, i) => [id, COLUMN_RECTS[i]!]),
  );
  const collisionRect = COLUMN_RECTS[overColumnIndex]!;
  const currentCoordinates = {
    x: centerX(collisionRect),
    y: COLUMN_TOP + 50,
  };

  const context = {
    droppableContainers: {
      getEnabled: () => COLUMN_IDS.map((id) => ({ id })),
    },
    droppableRects,
    collisionRect,
  };

  // Cast through unknown: we only populate the fields the getter actually reads.
  return { active: null, currentCoordinates, context } as unknown as Parameters<
    typeof boardKeyboardCoordinates
  >[1];
}

function keyEvent(code: string): KeyboardEvent {
  // preventDefault must exist; the getter calls it for arrow keys.
  return { code, preventDefault: vi.fn() } as unknown as KeyboardEvent;
}

describe('boardKeyboardCoordinates', () => {
  it('ignores non-arrow keys (returns undefined)', () => {
    const result = boardKeyboardCoordinates(keyEvent('KeyA'), makeArgs(2));
    expect(result).toBeUndefined();
  });

  it('ArrowRight moves to the centre of the next column', () => {
    const result = boardKeyboardCoordinates(keyEvent('ArrowRight'), makeArgs(1));
    expect(result).toEqual({
      x: centerX(COLUMN_RECTS[2]!),
      y: expect.any(Number),
    });
  });

  it('ArrowLeft moves to the centre of the previous column', () => {
    const result = boardKeyboardCoordinates(keyEvent('ArrowLeft'), makeArgs(2));
    expect(result?.x).toBe(centerX(COLUMN_RECTS[1]!));
  });

  it('does NOT wrap at the left edge (ArrowLeft from first column -> undefined)', () => {
    const result = boardKeyboardCoordinates(keyEvent('ArrowLeft'), makeArgs(0));
    expect(result).toBeUndefined();
  });

  it('does NOT wrap at the right edge (ArrowRight from last column -> undefined)', () => {
    const result = boardKeyboardCoordinates(keyEvent('ArrowRight'), makeArgs(4));
    expect(result).toBeUndefined();
  });

  it('ArrowUp nudges y upward by the keyboard step, leaving x unchanged', () => {
    const args = makeArgs(2);
    const before = args.currentCoordinates;
    const result = boardKeyboardCoordinates(keyEvent('ArrowUp'), args);
    expect(result).toEqual({ x: before.x, y: before.y - 25 });
  });

  it('ArrowDown nudges y downward by the keyboard step, leaving x unchanged', () => {
    const args = makeArgs(2);
    const before = args.currentCoordinates;
    const result = boardKeyboardCoordinates(keyEvent('ArrowDown'), args);
    expect(result).toEqual({ x: before.x, y: before.y + 25 });
  });

  it('calls preventDefault for handled arrow keys', () => {
    const event = keyEvent('ArrowRight');
    boardKeyboardCoordinates(event, makeArgs(1));
    expect(event.preventDefault).toHaveBeenCalled();
  });

  it('returns undefined when there are no measured columns', () => {
    const args = makeArgs(2);
    // Replace context with an empty droppable set.
    const emptyContext = {
      droppableContainers: { getEnabled: () => [] },
      droppableRects: new Map(),
      collisionRect: null,
    };
    const emptyArgs = {
      ...args,
      context: emptyContext,
    } as unknown as Parameters<typeof boardKeyboardCoordinates>[1];
    expect(boardKeyboardCoordinates(keyEvent('ArrowRight'), emptyArgs)).toBeUndefined();
  });

  it('keeps the destination y within the target column bounds', () => {
    const args = makeArgs(1);
    // Force the current y far below the column so we can see it clamped.
    (args.currentCoordinates as { y: number }).y = COLUMN_TOP + COLUMN_HEIGHT + 9999;
    const result = boardKeyboardCoordinates(keyEvent('ArrowRight'), args);
    expect(result).toBeDefined();
    const target = COLUMN_RECTS[2]!;
    expect(result!.y).toBeLessThanOrEqual(target.top + target.height - 1);
    expect(result!.y).toBeGreaterThanOrEqual(target.top + 1);
  });
});
