import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { CommentsPanel } from './CommentsPanel';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleComment, sampleUser } from '@/test/handlers';

// QA acceptance — CommentsPanel edit/delete (F-12, WAVE2 §5.2 / ADR-0015). Extends the developer smoke test:
// a blank edit is blocked client-side (no PUT), Cancel restores the original text, cancelling a delete does
// NOT call the API, a server 400 on edit surfaces a toast, and posting a new comment hits the create route.

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

describe('CommentsPanel edit/delete (acceptance)', () => {
  it('blocks a blank edit — Save is disabled and no PUT is sent', async () => {
    seedAuthToken('t');
    seedMe();
    let putCalled = false;
    server.use(
      http.put(`${API}/comments/:id`, () => {
        putCalled = true;
        return HttpResponse.json({ ...sampleComment }, { status: 200 });
      }),
    );

    const { user } = renderPanel();
    await screen.findByText('Looks fixed.');
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const textarea = screen.getByRole('textbox', { name: 'Edit comment' });
    await user.clear(textarea);
    await user.type(textarea, '   '); // whitespace only

    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    await user.click(save); // even if forced, saveEdit() no-ops on blank
    expect(putCalled).toBe(false);
  });

  it('Cancel discards the edit and restores the original body', async () => {
    seedAuthToken('t');
    seedMe();

    const { user } = renderPanel();
    await screen.findByText('Looks fixed.');
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const textarea = screen.getByRole('textbox', { name: 'Edit comment' });
    await user.clear(textarea);
    await user.type(textarea, 'a throwaway draft');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    // Back to read mode with the original text; the editor is gone.
    expect(await screen.findByText('Looks fixed.')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Edit comment' })).not.toBeInTheDocument();
  });

  it('cancelling the delete confirm does NOT call the delete API', async () => {
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

    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: /cancel/i }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(deleteCalled).toBe(false);
  });

  it('surfaces a server 400 on edit as a toast and stays in edit mode', async () => {
    seedAuthToken('t');
    seedMe();
    server.use(
      http.put(`${API}/comments/:id`, () =>
        HttpResponse.json(
          { error: { code: 'validation_error', message: 'Comment body is required.', errors: { body: ['Comment body is required.'] } } },
          { status: 400 },
        ),
      ),
    );

    const { user } = renderPanel();
    await screen.findByText('Looks fixed.');
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const textarea = screen.getByRole('textbox', { name: 'Edit comment' });
    await user.clear(textarea);
    await user.type(textarea, 'x');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    const toast = await screen.findByRole('status');
    expect(toast).toHaveTextContent(/required/i);
    // Still editing (the mutation failed, so we did not leave edit mode).
    expect(screen.getByRole('textbox', { name: 'Edit comment' })).toBeInTheDocument();
  });

  it('posts a new comment via the create route', async () => {
    seedAuthToken('t');
    seedMe();
    let postedBody: string | null = null;
    server.use(
      http.post(`${API}/tickets/:id/comments`, async ({ request }) => {
        const b = (await request.json()) as { body: string };
        postedBody = b.body;
        return HttpResponse.json({ ...sampleComment, id: 'new', body: b.body }, { status: 201 });
      }),
    );

    const { user } = renderPanel();
    await screen.findByText('Looks fixed.');

    await user.type(screen.getByLabelText('Add comment'), 'A brand new note');
    await user.click(screen.getByRole('button', { name: /post comment/i }));

    await waitFor(() => expect(postedBody).toBe('A brand new note'));
  });
});
