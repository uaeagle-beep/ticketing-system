import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { AppLayout } from './AppLayout';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleUser } from '@/test/handlers';

// The header shows displayName(name, email) (Feature 1) and gates the admin-only "Users" nav on
// isAdmin (ADR-0007). displayName's own unit tests cover the pure helper; this proves the SHELL wires
// it in — the name is shown when set, the email when not, and the Users link appears only for admins.

function Shell() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="/board" element={<div>Board content</div>} />
      </Route>
    </Routes>
  );
}

function seedMe(overrides: Partial<typeof sampleUser>) {
  server.use(
    http.get(`${API}/auth/me`, () => HttpResponse.json({ ...sampleUser, ...overrides }, { status: 200 })),
  );
}

describe('AppLayout header display name', () => {
  it('shows the display name when a name is set', async () => {
    seedAuthToken('t');
    seedMe({ name: 'Ada Lovelace', email: 'ada@dataart.com', isAdmin: true });
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    expect(screen.getByRole('button', { name: /Ada Lovelace/ })).toBeInTheDocument();
  });

  it('falls back to the email in the header when no name is set', async () => {
    seedAuthToken('t');
    seedMe({ name: null, email: 'noname@dataart.com', isAdmin: false });
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    expect(screen.getByRole('button', { name: /noname@dataart.com/ })).toBeInTheDocument();
  });

  it('shows the admin-only Users nav for an admin', async () => {
    seedAuthToken('t');
    seedMe({ name: null, email: 'boss@dataart.com', isAdmin: true });
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    expect(screen.getByRole('link', { name: 'Users' })).toBeInTheDocument();
  });

  it('hides the admin-only Users nav for a member', async () => {
    seedAuthToken('t');
    seedMe({ name: null, email: 'member@dataart.com', isAdmin: false });
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument();
  });

  it('shows the Help nav to every signed-in user (members included)', async () => {
    seedAuthToken('t');
    seedMe({ name: null, email: 'member@dataart.com', isAdmin: false });
    renderRoutes(<Shell />, { initialEntries: ['/board'] });

    await screen.findByText('Board content');
    expect(screen.getByRole('link', { name: 'Help' })).toHaveAttribute('href', '/help');
  });
});
