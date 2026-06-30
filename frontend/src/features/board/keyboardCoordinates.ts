// Keyboard coordinate getter for @dnd-kit's KeyboardSensor.
//
// We do NOT depend on @dnd-kit/sortable (it isn't installed — see package.json),
// so `sortableKeyboardCoordinates` is unavailable. The board is a horizontal row
// of five droppable columns; the only meaningful keyboard gesture is moving the
// dragged card LEFT/RIGHT between adjacent columns. This getter implements that:
//
//  - ArrowLeft / ArrowRight: jump to the centre of the previous / next droppable
//    column (ordered left-to-right by their measured x position).
//  - ArrowUp / ArrowDown: nudge within the current column.
//
// Returning new {x,y} coordinates causes dnd-kit to recompute collisions, so the
// `over` droppable updates and onDragEnd sees the destination column — exactly
// like a pointer drag. Returning the unchanged coordinates is a no-op ("stay put").
//
// Signature follows @dnd-kit/core v6: KeyboardCoordinateGetter receives the raw
// KeyboardEvent and { active, currentCoordinates, context }, where `context` is
// the sensor's snapshot of DndContext state (droppableContainers, droppableRects,
// collisionRect, …). We read measured rects from there and compute a target.

import { type KeyboardCoordinateGetter, type ClientRect } from '@dnd-kit/core';

const KEYBOARD_STEP = 25;

function centerX(rect: ClientRect): number {
  return rect.left + rect.width / 2;
}

export const boardKeyboardCoordinates: KeyboardCoordinateGetter = (
  event,
  { currentCoordinates, context },
) => {
  const { code } = event;
  if (code !== 'ArrowRight' && code !== 'ArrowLeft' && code !== 'ArrowUp' && code !== 'ArrowDown') {
    return undefined;
  }

  event.preventDefault();

  // Vertical nudges: move within the current column; the column the card is over
  // (and thus the destination state) is unchanged.
  if (code === 'ArrowUp') {
    return { ...currentCoordinates, y: currentCoordinates.y - KEYBOARD_STEP };
  }
  if (code === 'ArrowDown') {
    return { ...currentCoordinates, y: currentCoordinates.y + KEYBOARD_STEP };
  }

  // Horizontal moves: step to the adjacent droppable column.
  const { droppableContainers, droppableRects, collisionRect } = context;

  // x-ordered list of columns that have a measured rect.
  const columns = droppableContainers
    .getEnabled()
    .reduce<Array<{ id: string; rect: ClientRect }>>((acc, container) => {
      const rect = droppableRects.get(container.id);
      if (rect) acc.push({ id: String(container.id), rect });
      return acc;
    }, [])
    .sort((a, b) => a.rect.left - b.rect.left);

  if (columns.length === 0) return undefined;

  // Reference x is the dragged card's current horizontal centre.
  const referenceX = collisionRect ? centerX(collisionRect) : currentCoordinates.x;

  // Index of the column whose centre is closest to the card's current centre.
  let baseIndex = 0;
  let bestDistance = Infinity;
  columns.forEach((c, i) => {
    const d = Math.abs(centerX(c.rect) - referenceX);
    if (d < bestDistance) {
      bestDistance = d;
      baseIndex = i;
    }
  });

  const targetIndex = code === 'ArrowRight' ? baseIndex + 1 : baseIndex - 1;
  const target = columns[targetIndex];
  if (!target) {
    // At an edge — no wrap-around, stay put.
    return undefined;
  }

  return {
    x: centerX(target.rect),
    // Keep a sensible y inside the destination column so the collision resolves there.
    y: Math.min(
      Math.max(currentCoordinates.y, target.rect.top + 1),
      target.rect.top + target.rect.height - 1,
    ),
  };
};
