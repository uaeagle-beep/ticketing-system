import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { AppLayout } from './AppLayout';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

// The header bell shows the unread badge from GET /api/notifications/unread-count and links to
// /notifications (Wave 2 §9.1, ADR-0016 polling). This proves the shell wires the badge in.

function Shell() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="/board" element={<div>Board content</div>} />
        <Route path="/notifications" element={<div>Notifications content</div>} />
      </Route>
    </Routes>
  );
}

describe('AppLayout notification bell', () => {
  it('shows the unread badge count from the poll endpoint', async () => {
    seedAuthToken('t');
    server.use(
      http.get(`${API}/notifications/unread-count`, () =>
        HttpResponse.json({ unreadCount: 3 }, { status: 200 }),
      ),
    );
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    const bell = screen.getByRole('button', { name: /notifications \(3 unread\)/i });
    await waitFor(() => expect(bell).toHaveTextContent('3'));
  });

  it('navigates to /notifications when the bell is clicked', async () => {
    seedAuthToken('t');
    server.use(
      http.get(`${API}/notifications/unread-count`, () =>
        HttpResponse.json({ unreadCount: 0 }, { status: 200 }),
      ),
    );
    const { user } = renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    await user.click(screen.getByRole('button', { name: /^notifications$/i }));
    expect(await screen.findByText('Notifications content')).toBeInTheDocument();
  });
});
