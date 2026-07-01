import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { AppLayout } from './AppLayout';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';

// QA acceptance — the header notification bell badge (Wave 2 §9.1, ADR-0016). Extends the developer
// smoke test with the badge's edge behaviours: it hides at zero, shows the exact count, and caps at "99+".
// The badge value is aria-hidden decoration; the accessible name on the button carries the count.

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

function seedUnread(count: number) {
  server.use(
    http.get(`${API}/notifications/unread-count`, () =>
      HttpResponse.json({ unreadCount: count }, { status: 200 }),
    ),
  );
}

describe('NotificationBell badge (acceptance)', () => {
  it('hides the badge and uses the plain accessible name when there are zero unread', async () => {
    seedAuthToken('t');
    seedUnread(0);
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    const bell = await screen.findByRole('button', { name: /^notifications$/i });
    // No "(N unread)" suffix and no numeric badge text.
    expect(bell).toHaveAccessibleName('Notifications');
    expect(bell).not.toHaveTextContent(/\d/);
  });

  it('shows the exact unread count in the badge and accessible name', async () => {
    seedAuthToken('t');
    seedUnread(7);
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    const bell = await screen.findByRole('button', { name: /notifications \(7 unread\)/i });
    await waitFor(() => expect(bell).toHaveTextContent('7'));
  });

  it('caps the badge at "99+" for large counts', async () => {
    seedAuthToken('t');
    seedUnread(250);
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    const bell = await screen.findByRole('button', { name: /notifications \(250 unread\)/i });
    // The accessible name carries the true count; the visible badge caps at 99+.
    await waitFor(() => expect(bell).toHaveTextContent('99+'));
    expect(bell).not.toHaveTextContent('250');
  });
});
