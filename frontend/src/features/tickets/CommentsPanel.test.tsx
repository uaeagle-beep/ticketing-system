// CommentsPanel edit/delete (F-12, WAVE2 §5.2 / ADR-0015) — smoke coverage of the UI affordances.
// An author sees Edit + Delete on their own comment; edit is inline and calls PUT /api/comments/{id};
// an "edited" indicator shows when editedAt is set; delete confirms then calls DELETE. A non-author
// non-admin sees neither affordance. The full acceptance suite is the Tester's.

import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { CommentsPanel } from './CommentsPanel';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleComment, sampleUser } from '@/test/handlers';

function seedMe(overrides: Partial<typeof sampleUser> = {}) {
  server.use(
    http.get(`${API}/auth/me`, () => HttpResponse.json({ ...sampleUser, ...overrides }, { status: 200 })),
  );
}

function renderPanel() {
  return renderRoutes(
    <Routes>
      <Route path="/" element={<CommentsPanel ticketId={sampleComment.ticketId} />} />
    </Routes>,
    { initialEntries: ['/'] },
  );
}

describe('CommentsPanel — comment edit/delete (F-12)', () => {
  it('shows Edit and Delete on the author’s own comment', async () => {
    seedAuthToken('t');
    seedMe(); // default principal IS the comment author (same id)
    renderPanel();

    await screen.findByText('Looks fixed.');
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
  });

  it('edits a comment inline and PUTs the new body, then reflects it after refetch', async () => {
    seedAuthToken('t');
    seedMe();

    // Capture the PUT body, and have the subsequent list GET reflect the persisted edit
    // (server-state simulation: the refetch after the mutation returns the updated comment).
    let putBody: string | null = null;
    server.use(
      http.put(`${API}/comments/:id`, async ({ request }) => {
        const b = (await request.json()) as { body: string };
        putBody = b.body;
        return HttpResponse.json(
          { ...sampleComment, body: b.body, edited: true, editedAt: '2026-06-23T13:05:00Z' },
          { status: 200 },
        );
      }),
      http.get(`${API}/tickets/:id/comments`, () =>
        HttpResponse.json(
          putBody === null
            ? [sampleComment]
            : [{ ...sampleComment, body: putBody, edited: true, editedAt: '2026-06-23T13:05:00Z' }],
          { status: 200 },
        ),
      ),
    );

    const { user } = renderPanel();

    await screen.findByText('Looks fixed.');
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const textarea = screen.getByRole('textbox', { name: 'Edit comment' });
    await user.clear(textarea);
    await user.type(textarea, 'Actually still broken on Safari.');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(putBody).toBe('Actually still broken on Safari.'));
    await screen.findByText('Actually still broken on Safari.');
    expect(screen.getByText(/\(edited\)/)).toBeInTheDocument();
  });

  it('renders an "edited" indicator when editedAt is set', async () => {
    seedAuthToken('t');
    seedMe();
    server.use(
      http.get(`${API}/tickets/:id/comments`, () =>
        HttpResponse.json(
          [{ ...sampleComment, edited: true, editedAt: '2026-06-23T13:05:00Z' }],
          { status: 200 },
        ),
      ),
    );
    renderPanel();

    await screen.findByText('Looks fixed.');
    expect(screen.getByText(/\(edited\)/)).toBeInTheDocument();
  });

  it('deletes a comment after confirmation', async () => {
    seedAuthToken('t');
    seedMe();
    let deleteCalled = false;
    server.use(
      http.delete(`${API}/comments/:id`, () => {
        deleteCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderPanel();

    await screen.findByText('Looks fixed.');
    await user.click(screen.getByRole('button', { name: 'Delete' }));

    // ConfirmDialog opens; confirm the destructive action.
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteCalled).toBe(true));
  });

  it('hides Edit and Delete for a non-author non-admin', async () => {
    seedAuthToken('t');
    // A different, non-admin user => not the author, no moderation override.
    seedMe({ id: '00000000-0000-4000-8000-0000000000ff', isAdmin: false });
    renderPanel();

    await screen.findByText('Looks fixed.');
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
  });

  it('shows Delete (but not Edit) to an admin on another user’s comment', async () => {
    seedAuthToken('t');
    // Admin who is NOT the author: delete override yes, edit no (ADR-0015).
    seedMe({ id: '00000000-0000-4000-8000-0000000000aa', isAdmin: true });
    renderPanel();

    await screen.findByText('Looks fixed.');
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
  });
});
