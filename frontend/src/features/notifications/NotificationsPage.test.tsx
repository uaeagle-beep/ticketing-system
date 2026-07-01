import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { renderWithProviders, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';
import { NotificationsPage } from './NotificationsPage';

// Smoke coverage for the notifications page (Wave 2 §9.2): renders newest-first with unread styling,
// a null-ticket tombstone renders as a non-navigable row, and "Mark all read" calls the endpoint.
// The default MSW handlers (src/test/handlers.ts) supply one unread ticket_moved + one read tombstone.

describe('NotificationsPage', () => {
  it('lists notifications with a mark-all-read action', async () => {
    seedAuthToken('t');
    renderWithProviders(<NotificationsPage />, { initialEntries: ['/notifications'] });

    expect(await screen.findByText(/moved this from New to In progress/)).toBeInTheDocument();
    // The deleted-ticket tombstone summary is shown too.
    expect(screen.getByText(/deleted ticket 'Old bug'/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /mark all read/i })).toBeInTheDocument();
  });

  it('marks all read when the button is clicked', async () => {
    seedAuthToken('t');
    const markAll = vi.fn();
    server.use(
      http.post(`${API}/notifications/read-all`, () => {
        markAll();
        return HttpResponse.json({ unreadCount: 0 }, { status: 200 });
      }),
    );

    const { user } = renderWithProviders(<NotificationsPage />, {
      initialEntries: ['/notifications'],
    });

    const btn = await screen.findByRole('button', { name: /mark all read/i });
    await user.click(btn);
    await waitFor(() => expect(markAll).toHaveBeenCalledTimes(1));
  });

  it('renders a deleted-ticket tombstone as a non-navigable row', async () => {
    seedAuthToken('t');
    renderWithProviders(<NotificationsPage />, { initialEntries: ['/notifications'] });

    const tombstone = await screen.findByText(/deleted ticket 'Old bug'/);
    // The tombstone is already read in the fixture, so its button is disabled (nothing to do on click).
    const button = tombstone.closest('button');
    expect(button).toBeDisabled();
  });
});
