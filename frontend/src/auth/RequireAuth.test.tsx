import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { RequireAuth } from './RequireAuth';
import { renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleUser } from '@/test/handlers';

function ProtectedTree() {
  return (
    <Routes>
      <Route element={<RequireAuth />}>
        <Route path="/protected" element={<div>Protected content</div>} />
      </Route>
      <Route path="/login" element={<div>Login screen</div>} />
    </Routes>
  );
}

describe('RequireAuth', () => {
  it('redirects to /login when there is no token (unauthenticated)', async () => {
    // Token is cleared by the global setup -> AuthProvider resolves to
    // unauthenticated without calling /me.
    renderRoutes(<ProtectedTree />, { initialEntries: ['/protected'] });

    expect(await screen.findByText('Login screen')).toBeInTheDocument();
    expect(screen.queryByText('Protected content')).not.toBeInTheDocument();
  });

  it('renders the protected outlet once an authenticated, verified session resolves', async () => {
    // Seed a token; AuthProvider bootstraps via GET /api/auth/me (mocked).
    seedAuthToken('valid-token');

    renderRoutes(<ProtectedTree />, { initialEntries: ['/protected'] });

    expect(await screen.findByText('Protected content')).toBeInTheDocument();
    expect(screen.queryByText('Login screen')).not.toBeInTheDocument();
  });

  it('redirects to /login when the bootstrap /me reports an unverified account', async () => {
    seedAuthToken('valid-but-unverified');
    server.use(
      http.get(`${API}/auth/me`, () =>
        HttpResponse.json({ ...sampleUser, emailVerified: false }, { status: 200 }),
      ),
    );

    renderRoutes(<ProtectedTree />, { initialEntries: ['/protected'] });

    // AuthProvider clears the token and drops to unauthenticated -> redirect.
    expect(await screen.findByText('Login screen')).toBeInTheDocument();
  });

  it('redirects to /login when the token is rejected (401 on /me)', async () => {
    seedAuthToken('expired-token');
    server.use(
      http.get(`${API}/auth/me`, () =>
        HttpResponse.json(
          { error: { code: 'unauthorized', message: 'expired' } },
          { status: 401 },
        ),
      ),
    );

    renderRoutes(<ProtectedTree />, { initialEntries: ['/protected'] });

    expect(await screen.findByText('Login screen')).toBeInTheDocument();
  });
});
