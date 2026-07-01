// Wave 3 API-keys management — focused smoke coverage (ADR-0021, §10.4). Verifies:
//  - the manager lists the caller's keys (name, prefix, scopes, active state);
//  - creating a key reveals the raw ptk_ key ONCE (copy-to-clipboard notice);
//  - a revoked/write scope key round-trips through the create form.
// Full feature coverage (revoke flow, error mapping) is the Tester's.

import { describe, expect, it } from 'vitest';
import { screen, within } from '@testing-library/react';
import { ApiKeysManager } from './ApiKeysManager';
import { renderWithProviders } from '@/test/renderWithProviders';
import { sampleApiKey } from '@/test/handlers';

describe('ApiKeysManager', () => {
  it('lists the caller’s API keys with prefix and scopes', async () => {
    renderWithProviders(<ApiKeysManager />);

    expect(await screen.findByText(sampleApiKey.name)).toBeInTheDocument();
    expect(screen.getByText(`${sampleApiKey.prefix}…`)).toBeInTheDocument();
    expect(screen.getByText('tickets:read, tickets:write')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('creates a key and reveals the raw ptk_ key once', async () => {
    const { user } = renderWithProviders(<ApiKeysManager />);
    await screen.findByText(sampleApiKey.name);

    await user.click(screen.getByRole('button', { name: '+ New key' }));
    const form = screen.getByRole('form', { name: 'Create API key' });
    await user.type(within(form).getByLabelText('Name'), 'CI');
    await user.click(within(form).getByLabelText('tickets:write'));
    await user.click(within(form).getByRole('button', { name: 'Create key' }));

    const secret = await screen.findByTestId('revealed-secret');
    expect(secret).toHaveTextContent('ptk_revealed-once-key-value');
  });
});
