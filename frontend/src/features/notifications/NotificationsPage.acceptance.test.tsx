import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleNotificationList } from '@/test/handlers';
import type { NotificationList } from '@/api/types';
import { NotificationsPage } from './NotificationsPage';

// QA acceptance — the notifications page (Wave 2 §9.2, ADR-0013/0016). Extends the developer smoke test:
// clicking an UNREAD navigable notification marks it read AND navigates to its ticket; an UNREAD tombstone
// (null ticketId) is markable but NON-navigable; "Load more" pages via the cursor; the empty state renders
// when there are none; and the error state offers a retry.

function Shell() {
  return (
    <Routes>
      <Route path="/notifications" element={<NotificationsPage />} />
      <Route path="/tickets/:id" element={<div>Ticket page</div>} />
    </Routes>
  );
}

describe('NotificationsPage (acceptance)', () => {
  it('marks read and navigates to the ticket when an unread navigable item is clicked', async () => {
    seedAuthToken('t');
    let readId: string | null = null;
    server.use(
      http.post(`${API}/notifications/:id/read`, ({ params }) => {
        readId = String(params.id);
        return HttpResponse.json({ unreadCount: 0 }, { status: 200 });
      }),
    );

    const { user } = renderRoutes(<Shell />, { initialEntries: ['/notifications'] });

    // The first fixture item is an unread, navigable ticket_moved notification.
    const row = await screen.findByText(/moved this from New to In progress/);
    await user.click(row);

    await waitFor(() => expect(readId).toBe('nt01-moved'));
    expect(await screen.findByText('Ticket page')).toBeInTheDocument();
  });

  it('marks an unread tombstone read but does NOT navigate (non-navigable)', async () => {
    seedAuthToken('t');
    // An unread tombstone (null ticketId, no readAt) — markable but non-navigable.
    const list: NotificationList = {
      ...sampleNotificationList,
      items: [
        {
          ...sampleNotificationList.items[1]!,
          id: 'nt-unread-tombstone',
          readAt: null,
        },
      ],
      unreadCount: 1,
    };
    let readId: string | null = null;
    server.use(
      http.get(`${API}/notifications`, () => HttpResponse.json(list, { status: 200 })),
      http.post(`${API}/notifications/:id/read`, ({ params }) => {
        readId = String(params.id);
        return HttpResponse.json({ unreadCount: 0 }, { status: 200 });
      }),
    );

    const { user } = renderRoutes(<Shell />, { initialEntries: ['/notifications'] });

    const tombstone = await screen.findByText(/deleted ticket 'Old bug'/);
    await user.click(tombstone);

    await waitFor(() => expect(readId).toBe('nt-unread-tombstone'));
    // No navigation happened — the ticket route never rendered.
    expect(screen.queryByText('Ticket page')).not.toBeInTheDocument();
  });

  it('loads the next page via the cursor when "Load more" is clicked', async () => {
    seedAuthToken('t');
    const page1: NotificationList = {
      items: [
        {
          id: 'p1-a',
          eventType: 'ticket_moved',
          summary: 'Alex moved this from New to In progress',
          ticketId: 'tk-1',
          commentId: null,
          actorId: 'u2',
          actorDisplayName: 'Alex',
          createdAt: '2026-06-23T14:00:00Z',
          readAt: '2026-06-23T14:01:00Z',
        },
      ],
      unreadCount: 0,
      hasMore: true,
      nextCursor: 'CURSOR-1',
    };
    const page2: NotificationList = {
      items: [
        {
          id: 'p2-a',
          eventType: 'comment_added',
          summary: 'Sam commented',
          ticketId: 'tk-1',
          commentId: 'cm-1',
          actorId: 'u3',
          actorDisplayName: 'Sam',
          createdAt: '2026-06-23T13:00:00Z',
          readAt: '2026-06-23T13:01:00Z',
        },
      ],
      unreadCount: 0,
      hasMore: false,
      nextCursor: null,
    };
    let sawCursor: string | null = null;
    server.use(
      http.get(`${API}/notifications`, ({ request }) => {
        const url = new URL(request.url);
        const cursor = url.searchParams.get('cursor');
        if (cursor) {
          sawCursor = cursor;
          return HttpResponse.json(page2, { status: 200 });
        }
        return HttpResponse.json(page1, { status: 200 });
      }),
    );

    const { user } = renderRoutes(<Shell />, { initialEntries: ['/notifications'] });

    await screen.findByText(/moved this from New to In progress/);
    await user.click(screen.getByRole('button', { name: /load more/i }));

    expect(await screen.findByText('Sam commented')).toBeInTheDocument();
    expect(sawCursor).toBe('CURSOR-1');
    // The first page's item is still shown (appended, not replaced).
    expect(screen.getByText(/moved this from New to In progress/)).toBeInTheDocument();
  });

  it('renders the empty state when there are no notifications', async () => {
    seedAuthToken('t');
    server.use(
      http.get(`${API}/notifications`, () =>
        HttpResponse.json(
          { items: [], unreadCount: 0, hasMore: false, nextCursor: null },
          { status: 200 },
        ),
      ),
    );

    renderRoutes(<Shell />, { initialEntries: ['/notifications'] });
    expect(await screen.findByText(/all caught up/i)).toBeInTheDocument();
  });

  it('renders an error state with a retry that refetches', async () => {
    seedAuthToken('t');
    let calls = 0;
    server.use(
      http.get(`${API}/notifications`, () => {
        calls += 1;
        if (calls === 1) {
          return HttpResponse.json(
            { error: { code: 'unauthorized', message: 'nope' } },
            { status: 500 },
          );
        }
        return HttpResponse.json(sampleNotificationList, { status: 200 });
      }),
    );

    const { user } = renderRoutes(<Shell />, { initialEntries: ['/notifications'] });

    const retry = await screen.findByRole('button', { name: /retry|try again/i });
    await user.click(retry);
    expect(await screen.findByText(/moved this from New to In progress/)).toBeInTheDocument();
  });

  it('disables "Mark all read" when there is nothing unread', async () => {
    seedAuthToken('t');
    server.use(
      http.get(`${API}/notifications`, () =>
        HttpResponse.json(
          { ...sampleNotificationList, unreadCount: 0, items: sampleNotificationList.items.map((n) => ({ ...n, readAt: '2026-06-23T13:10:00Z' })) },
          { status: 200 },
        ),
      ),
    );
    renderRoutes(<Shell />, { initialEntries: ['/notifications'] });

    await screen.findByText(/moved this from New to In progress/);
    expect(screen.getByRole('button', { name: /mark all read/i })).toBeDisabled();
  });
});
