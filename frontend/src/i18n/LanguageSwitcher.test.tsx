// LanguageSwitcher tests (Wave 3, ADR-0022): the switcher control renders both languages, switching
// flips the active i18next language, persists to localStorage, and — when authenticated — mirrors the
// choice to PUT /api/me/profile (preserving the display name). Also proves that a rendered UI string
// (the AppLayout nav) flips to Ukrainian after switching, end-to-end through the provider.

import { describe, expect, it, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { AppLayout } from '@/components/AppLayout';
import { renderWithProviders, renderRoutes, Route, Routes, seedAuthToken } from '@/test/renderWithProviders';
import { server } from '@/test/server';
import { API, sampleUser } from '@/test/handlers';
import { LANGUAGE_STORAGE_KEY, currentLanguage } from './config';

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('renders a language control offering Ukrainian and English', () => {
    renderWithProviders(<LanguageSwitcher />);
    const select = screen.getByRole('combobox', { name: /language/i });
    expect(select).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Українська' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'English' })).toBeInTheDocument();
  });

  it('switching to Ukrainian updates the active language and persists to localStorage', async () => {
    const { user } = renderWithProviders(<LanguageSwitcher />);
    const select = screen.getByRole('combobox', { name: /language/i });

    await user.selectOptions(select, 'uk');

    await waitFor(() => expect(currentLanguage()).toBe('uk'));
    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('uk');
  });

  it('mirrors the choice to PUT /api/me/profile when authenticated (preserving the name)', async () => {
    let profileBody: { name: string | null; locale?: string | null } | null = null;
    server.use(
      http.get(`${API}/auth/me`, () =>
        HttpResponse.json({ ...sampleUser, name: 'Ada Lovelace' }, { status: 200 }),
      ),
      http.put(`${API}/me/profile`, async ({ request }) => {
        profileBody = (await request.json()) as typeof profileBody;
        return HttpResponse.json({ ...sampleUser, name: 'Ada Lovelace', locale: 'uk' }, { status: 200 });
      }),
    );
    seedAuthToken('t');

    const { user } = renderWithProviders(<LanguageSwitcher />);
    // Wait for AuthProvider to resolve /me (authenticated).
    await waitFor(() => expect(screen.getByRole('combobox', { name: /language/i })).toBeEnabled());

    await user.selectOptions(screen.getByRole('combobox', { name: /language/i }), 'uk');

    await waitFor(() => expect(profileBody).not.toBeNull());
    expect(profileBody).toEqual({ name: 'Ada Lovelace', locale: 'uk' });
  });

  it('does NOT call the profile API when unauthenticated', async () => {
    let called = false;
    server.use(
      http.put(`${API}/me/profile`, () => {
        called = true;
        return HttpResponse.json({ ...sampleUser }, { status: 200 });
      }),
    );

    const { user } = renderWithProviders(<LanguageSwitcher />);
    await user.selectOptions(screen.getByRole('combobox', { name: /language/i }), 'uk');

    await waitFor(() => expect(currentLanguage()).toBe('uk'));
    expect(called).toBe(false);
  });

  it('flips the AppLayout nav to Ukrainian after switching (end-to-end)', async () => {
    seedAuthToken('t');
    function Shell() {
      return (
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/board" element={<div>Board content</div>} />
          </Route>
        </Routes>
      );
    }
    const { user } = renderRoutes(<Shell />, { initialEntries: ['/board'] });
    await screen.findByText('Board content');

    // English nav initially (en-pinned).
    expect(screen.getByRole('link', { name: 'Board' })).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: /language|мова/i }), 'uk');

    // Ukrainian nav after switching (common:nav.board => "Дошка").
    await waitFor(() => expect(screen.getByRole('link', { name: 'Дошка' })).toBeInTheDocument());
    expect(screen.queryByRole('link', { name: 'Board' })).not.toBeInTheDocument();
  });
});
