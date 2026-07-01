// Wave 3 webhooks management — focused smoke coverage (ADR-0021, §10.4). Verifies:
//  - the manager lists a team's subscriptions (url + events + active badge);
//  - creating a webhook reveals the signing secret ONCE (copy-to-clipboard notice);
//  - the deliveries drawer shows status/attempts/last error;
//  - a "ping" enqueues a test delivery (success toast).
// Full feature coverage (edit/rotate/delete flows, error mapping) is the Tester's.

import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { WebhooksManager } from './WebhooksManager';
import { renderWithProviders } from '@/test/renderWithProviders';
import { sampleTeam, sampleWebhook } from '@/test/handlers';

describe('WebhooksManager', () => {
  it('lists a team’s subscriptions with url, events and active state', async () => {
    renderWithProviders(<WebhooksManager teamId={sampleTeam.id} />);

    expect(await screen.findByText(sampleWebhook.url)).toBeInTheDocument();
    expect(screen.getByText('ticket_moved, comment_added')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('creates a webhook and reveals the signing secret once', async () => {
    const { user } = renderWithProviders(<WebhooksManager teamId={sampleTeam.id} />);
    await screen.findByText(sampleWebhook.url);

    await user.click(screen.getByRole('button', { name: '+ Add webhook' }));
    const form = screen.getByRole('form', { name: 'Create webhook' });
    await user.type(within(form).getByLabelText('Endpoint URL'), 'https://hooks.example.com/x');
    await user.click(within(form).getByLabelText('ticket_moved'));
    await user.click(within(form).getByRole('button', { name: 'Create webhook' }));

    // The revealed secret is shown once with a copy affordance.
    const secret = await screen.findByTestId('revealed-secret');
    expect(secret).toHaveTextContent('whsec_revealed-once-abc123');
  });

  it('shows delivery status/attempts in the deliveries drawer', async () => {
    const { user } = renderWithProviders(<WebhooksManager teamId={sampleTeam.id} />);
    await screen.findByText(sampleWebhook.url);

    await user.click(screen.getByRole('button', { name: 'Deliveries' }));

    expect(await screen.findByText('delivered')).toBeInTheDocument();
    expect(screen.getByText('failed')).toBeInTheDocument();
    expect(screen.getByText('HTTP 500')).toBeInTheDocument();
  });

  it('pings a subscription (enqueues a test delivery)', async () => {
    const { user } = renderWithProviders(<WebhooksManager teamId={sampleTeam.id} />);
    await screen.findByText(sampleWebhook.url);

    await user.click(screen.getByRole('button', { name: 'Ping' }));

    await waitFor(() => expect(screen.getByText('Test ping enqueued.')).toBeInTheDocument());
  });
});
