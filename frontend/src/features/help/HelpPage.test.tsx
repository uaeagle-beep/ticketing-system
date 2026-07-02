import { describe, it, expect } from 'vitest';
import { screen } from '@testing-library/react';
import { HelpPage } from './HelpPage';
import { renderWithProviders } from '@/test/renderWithProviders';
import i18n from '@/i18n/config';

// The Help page renders the full User & Administrator Guide (docs/USER_GUIDE{,.en}.md) in the active
// UI language. The global test setup pins i18n to 'en' and resets it after each test.

describe('HelpPage', () => {
  it('renders the guide in English by default (pinned by the test setup)', () => {
    renderWithProviders(<HelpPage />);

    // The page supplies its own localized title ("Help").
    expect(screen.getByRole('heading', { level: 1, name: 'Help' })).toBeInTheDocument();
    // A known section from the English guide is rendered as a heading.
    expect(screen.getByRole('heading', { name: /System overview/ })).toBeInTheDocument();
    // Tables render (GFM): the Roles table has a "Member" row.
    expect(screen.getByRole('cell', { name: /Member/ })).toBeInTheDocument();
  });

  it("drops the guide's own top-level title (we render the app title instead)", () => {
    renderWithProviders(<HelpPage />);
    // The stripped H1 "Ticket Tracker — User & Administrator Guide" must not appear as a heading.
    expect(
      screen.queryByRole('heading', { name: /User & Administrator Guide/ }),
    ).not.toBeInTheDocument();
  });

  it('opens external links in a new tab with a safe rel', () => {
    renderWithProviders(<HelpPage />);
    const prodLink = screen.getByRole('link', { name: /honcharenko\.pp\.ua/ });
    expect(prodLink).toHaveAttribute('target', '_blank');
    expect(prodLink).toHaveAttribute('rel', expect.stringContaining('noopener'));
  });

  it('renders the guide in Ukrainian when the UI language is uk', async () => {
    await i18n.changeLanguage('uk');
    renderWithProviders(<HelpPage />);

    expect(screen.getByRole('heading', { level: 1, name: 'Допомога' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Огляд системи/ })).toBeInTheDocument();
  });
});
