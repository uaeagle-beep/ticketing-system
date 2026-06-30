// Shared render helper that wraps a component in the same provider stack the
// real app uses (see src/main.tsx), but with a MemoryRouter for routing control
// and a per-test QueryClient with retries disabled for deterministic tests.
//
// Provider order mirrors main.tsx:
//   QueryClientProvider > Router > ToastProvider > AuthProvider > children
//
// AuthProvider bootstraps from the auth-token store; the global test setup
// clears the token after every test, so by default it resolves to
// `unauthenticated` without an extra /me round-trip. To exercise an
// authenticated tree, seed the token first (see seedAuthToken) — AuthProvider
// will then call GET /api/auth/me (mocked by MSW) and resolve to authenticated.

import type { ReactElement, ReactNode } from 'react';
import { render, type RenderOptions, type RenderResult } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from '@/auth/AuthContext';
import { ToastProvider } from '@/components/toast/ToastContext';
import { setToken } from '@/api/tokenStore';

export function makeTestQueryClient(): QueryClient {
  // Retries off so deliberately-failing requests reject immediately (no waiting
  // through exponential backoff). gcTime Infinity keeps cache stable across a
  // test. (TanStack Query v5 has no `logger` option; nothing to silence here.)
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity, staleTime: 0 },
      mutations: { retry: false },
    },
  });
}

/** Seed the auth-token mirror so AuthProvider bootstraps an authenticated user. */
export function seedAuthToken(token = 'test-token'): void {
  setToken(token);
}

interface RenderWithProvidersOptions extends Omit<RenderOptions, 'wrapper'> {
  /** Initial history entries for the MemoryRouter (default ['/']). */
  initialEntries?: string[];
  /** Optional explicit QueryClient (defaults to a fresh retry-off client). */
  queryClient?: QueryClient;
}

export interface RenderWithProvidersResult extends RenderResult {
  queryClient: QueryClient;
  user: ReturnType<typeof userEvent.setup>;
}

function Providers({
  children,
  initialEntries,
  queryClient,
}: {
  children: ReactNode;
  initialEntries: string[];
  queryClient: QueryClient;
}) {
  return (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={initialEntries}>
        <ToastProvider>
          <AuthProvider>{children}</AuthProvider>
        </ToastProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

export function renderWithProviders(
  ui: ReactElement,
  { initialEntries = ['/'], queryClient, ...options }: RenderWithProvidersOptions = {},
): RenderWithProvidersResult {
  const client = queryClient ?? makeTestQueryClient();
  const result = render(ui, {
    wrapper: ({ children }) => (
      <Providers initialEntries={initialEntries} queryClient={client}>
        {children}
      </Providers>
    ),
    ...options,
  });
  return { ...result, queryClient: client, user: userEvent.setup() };
}

/**
 * Render a routing tree (a `<Routes>` element) inside the full provider stack.
 * Useful for guard tests (RequireAuth) where we need to observe a redirect to
 * another route. The passed element is rendered verbatim — pass a complete
 * `<Routes>...</Routes>` subtree.
 */
export function renderRoutes(
  routes: ReactElement,
  options: RenderWithProvidersOptions = {},
): RenderWithProvidersResult {
  return renderWithProviders(routes, options);
}

// Re-export the route primitives so route-based tests don't import the router
// package separately.
export { Route, Routes };
