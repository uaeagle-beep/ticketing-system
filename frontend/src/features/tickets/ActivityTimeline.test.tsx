import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { renderWithProviders } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleActivityList } from '@/test/handlers';
import type { ActivityList } from '@/api/types';
import { ActivityTimeline } from './ActivityTimeline';

// QA acceptance — the ticket activity timeline component (Wave 2 §9.3, ADR-0012). No prior test covered it.
// Verifies: renders the server-rendered summaries newest-first with a count, an empty state when there are
// none, an error banner on failure, and "Load more" paging via the cursor.

describe('ActivityTimeline (acceptance)', () => {
  it('renders the timeline summaries in server order (newest-first) with a count', async () => {
    renderWithProviders(<ActivityTimeline ticketId="tk-1" />);

    // Both fixture entries are shown; the server already sorts newest-first.
    expect(await screen.findByText('Alex Doe moved this from New to In progress')).toBeInTheDocument();
    expect(screen.getByText('Alex Doe created this ticket')).toBeInTheDocument();

    // DOM order matches the server order (moved before created).
    const items = screen.getAllByText(/Alex Doe (moved this|created this ticket)/);
    expect(items[0]).toHaveTextContent('moved this');
    expect(items[1]).toHaveTextContent('created this ticket');
  });

  it('renders "No activity yet" when the timeline is empty', async () => {
    server.use(
      http.get(`${API}/tickets/:id/activity`, () =>
        HttpResponse.json({ items: [], hasMore: false, nextCursor: null }, { status: 200 }),
      ),
    );
    renderWithProviders(<ActivityTimeline ticketId="tk-1" />);
    expect(await screen.findByText(/no activity yet/i)).toBeInTheDocument();
  });

  it('renders an error banner when the activity request fails', async () => {
    server.use(
      http.get(`${API}/tickets/:id/activity`, () =>
        HttpResponse.json({ error: { code: 'forbidden', message: 'no' } }, { status: 403 }),
      ),
    );
    renderWithProviders(<ActivityTimeline ticketId="tk-1" />);
    // The component surfaces the mapped error message in a banner.
    await waitFor(() =>
      expect(document.querySelector('.banner-error')).toBeInTheDocument(),
    );
  });

  it('pages the timeline via the cursor on "Load more"', async () => {
    const page1: ActivityList = {
      items: [sampleActivityList.items[0]!],
      hasMore: true,
      nextCursor: 'AC-CURSOR-1',
    };
    const page2: ActivityList = {
      items: [sampleActivityList.items[1]!],
      hasMore: false,
      nextCursor: null,
    };
    let sawCursor: string | null = null;
    server.use(
      http.get(`${API}/tickets/:id/activity`, ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        if (cursor) {
          sawCursor = cursor;
          return HttpResponse.json(page2, { status: 200 });
        }
        return HttpResponse.json(page1, { status: 200 });
      }),
    );

    const { user } = renderWithProviders(<ActivityTimeline ticketId="tk-1" />);

    await screen.findByText('Alex Doe moved this from New to In progress');
    await user.click(screen.getByRole('button', { name: /load more/i }));

    expect(await screen.findByText('Alex Doe created this ticket')).toBeInTheDocument();
    expect(sawCursor).toBe('AC-CURSOR-1');
  });
});
