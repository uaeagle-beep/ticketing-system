import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { renderWithProviders, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API } from '@/test/handlers';
import { AccountPage } from './AccountPage';

// Smoke coverage for the Account page email-notifications toggle (Wave 2 §6.8): the checkbox reflects
// GET /api/me/notification-settings and toggling it PUTs the new value.

describe('AccountPage — notification settings', () => {
  it('reflects the current email toggle and updates on change', async () => {
    seedAuthToken('t');
    const put = vi.fn();
    server.use(
      http.get(`${API}/me/notification-settings`, () =>
        HttpResponse.json({ emailNotificationsEnabled: true }, { status: 200 }),
      ),
      http.put(`${API}/me/notification-settings`, async ({ request }) => {
        const body = (await request.json()) as { emailNotificationsEnabled: boolean };
        put(body.emailNotificationsEnabled);
        return HttpResponse.json(body, { status: 200 });
      }),
    );

    const { user } = renderWithProviders(<AccountPage />, { initialEntries: ['/account'] });

    const checkbox = await screen.findByRole('checkbox', { name: /email me notification digests/i });
    await waitFor(() => expect(checkbox).toBeChecked());

    await user.click(checkbox);
    await waitFor(() => expect(put).toHaveBeenCalledWith(false));
  });
});
