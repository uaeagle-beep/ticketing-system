import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { RequireAuth } from './RequireAuth';
import { RequireAdmin } from './RequireAdmin';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleUser } from '@/test/handlers';

function AdminTree() {
  return (
    <Routes>
      <Route element={<RequireAuth />}>
        <Route element={<RequireAdmin />}>
          <Route path="/users" element={<div>Users admin zone</div>} />
        </Route>
        <Route path="/board" element={<div>Board</div>} />
      </Route>
    </Routes>
  );
}

describe('RequireAdmin', () => {
  it('renders the admin route for an admin user', async () => {
    seedAuthToken('admin-token');
    // Default /me returns an admin (sampleUser.isAdmin = true).
    renderRoutes(<AdminTree />, { initialEntries: ['/users'] });

    expect(await screen.findByText('Users admin zone')).toBeInTheDocument();
  });

  it('redirects a non-admin (member) away from the admin route to the board', async () => {
    seedAuthToken('member-token');
    server.use(
      http.get(`${API}/auth/me`, () =>
        HttpResponse.json({ ...sampleUser, isAdmin: false, teams: [] }, { status: 200 }),
      ),
    );

    renderRoutes(<AdminTree />, { initialEntries: ['/users'] });

    expect(await screen.findByText('Board')).toBeInTheDocument();
    expect(screen.queryByText('Users admin zone')).not.toBeInTheDocument();
  });
});
