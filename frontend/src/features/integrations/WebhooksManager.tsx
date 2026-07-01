// Webhook management panel for a team (Wave 3, ADR-0021, §10.4). Member-managed: any member of the team
// (or an admin) can create, toggle-active, delete a subscription, view its recent deliveries, and send a
// test ping. Rendered as an expandable panel on the Teams page row (mirroring the labels/WIP managers).
// Create is an inline form (url + event-type checkboxes + active); the signing secret is revealed ONCE.
// Errors surface as toasts.

import { useState, type FormEvent } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { WEBHOOK_EVENT_TYPES, type WebhookSubscription } from '@/api/types';
import { useWebhooks, useWebhookDeliveries, useWebhookMutations } from './useWebhooks';
import { SecretReveal } from './SecretReveal';
import { errorMessage } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { LoadingState, ErrorState } from '@/components/States';
import { formatUtc } from '@/lib/time';

export function WebhooksManager({ teamId }: { teamId: string }) {
  const { t } = useTranslation('integrations');
  const toast = useToast();
  const webhooksQuery = useWebhooks(teamId);
  const { create, update, remove, ping } = useWebhookMutations(teamId);

  const [showCreate, setShowCreate] = useState(false);
  const [url, setUrl] = useState('');
  const [selectedEvents, setSelectedEvents] = useState<Set<string>>(new Set());
  const [wildcard, setWildcard] = useState(false);
  const [revealedSecret, setRevealedSecret] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<WebhookSubscription | null>(null);
  const [deliveriesFor, setDeliveriesFor] = useState<string | null>(null);

  const toggleEvent = (code: string) => {
    setSelectedEvents((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });
  };

  const resetCreate = () => {
    setUrl('');
    setSelectedEvents(new Set());
    setWildcard(false);
    setShowCreate(false);
  };

  const submitCreate = (e: FormEvent) => {
    e.preventDefault();
    const eventTypes = wildcard ? ['*'] : Array.from(selectedEvents);
    if (!url.trim() || eventTypes.length === 0) return;
    create.mutate(
      { url: url.trim(), eventTypes, active: true },
      {
        onSuccess: (res) => {
          toast.showSuccess(t('webhooks.created'));
          setRevealedSecret(res.secret);
          resetCreate();
        },
        onError: (err) => toast.showError(errorMessage(err)),
      },
    );
  };

  const toggleActive = (sub: WebhookSubscription) => {
    update.mutate(
      { id: sub.id, body: { active: !sub.active } },
      {
        onSuccess: () => toast.showSuccess(sub.active ? t('webhooks.disabled') : t('webhooks.enabled')),
        onError: (err) => toast.showError(errorMessage(err)),
      },
    );
  };

  const rotateSecret = (sub: WebhookSubscription) => {
    update.mutate(
      { id: sub.id, body: { rotateSecret: true } },
      {
        onSuccess: (res) => {
          if (res.secret) setRevealedSecret(res.secret);
          toast.showSuccess(t('webhooks.secretRotated'));
        },
        onError: (err) => toast.showError(errorMessage(err)),
      },
    );
  };

  const sendPing = (sub: WebhookSubscription) => {
    ping.mutate(sub.id, {
      onSuccess: () => {
        toast.showSuccess(t('webhooks.pingEnqueued'));
        setDeliveriesFor(sub.id);
      },
      onError: (err) => toast.showError(errorMessage(err)),
    });
  };

  const confirmDelete = () => {
    if (!deleteTarget) return;
    remove.mutate(deleteTarget.id, {
      onSuccess: () => {
        toast.showSuccess(t('webhooks.deleted'));
        setDeleteTarget(null);
      },
      onError: (err) => {
        setDeleteTarget(null);
        toast.showError(errorMessage(err));
      },
    });
  };

  const subscriptions = webhooksQuery.data ?? [];

  return (
    <div className="webhooks-manager">
      <div className="row" style={{ marginBottom: 8 }}>
        <p className="muted" style={{ margin: 0 }}>
          {t('webhooks.intro')}
        </p>
        <div className="spacer" />
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={() => setShowCreate((v) => !v)}
        >
          {t('webhooks.addWebhook')}
        </button>
      </div>

      {revealedSecret ? (
        <div style={{ marginBottom: 12 }}>
          <SecretReveal secret={revealedSecret} label={t('webhooks.secretLabel')} />
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            style={{ marginTop: 6 }}
            onClick={() => setRevealedSecret(null)}
          >
            {t('webhooks.done')}
          </button>
        </div>
      ) : null}

      {showCreate ? (
        <form className="webhook-create-form" onSubmit={submitCreate} aria-label={t('webhooks.form.ariaLabel')}>
          <div className="field">
            <label htmlFor={`webhook-url-${teamId}`}>{t('webhooks.form.url')}</label>
            <input
              id={`webhook-url-${teamId}`}
              className="input"
              placeholder={t('webhooks.form.urlPlaceholder')}
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              disabled={create.isPending}
            />
          </div>
          <fieldset className="field" style={{ border: 0, padding: 0, margin: 0 }}>
            <legend>{t('webhooks.form.events')}</legend>
            <label className="row" style={{ gap: 6, alignItems: 'center' }}>
              <input
                type="checkbox"
                checked={wildcard}
                onChange={(e) => setWildcard(e.target.checked)}
                disabled={create.isPending}
              />
              <span>{t('webhooks.form.allEvents')}</span>
            </label>
            {!wildcard ? (
              <div className="webhook-event-grid">
                {WEBHOOK_EVENT_TYPES.map((code) => (
                  <label key={code} className="row" style={{ gap: 6, alignItems: 'center' }}>
                    <input
                      type="checkbox"
                      checked={selectedEvents.has(code)}
                      onChange={() => toggleEvent(code)}
                      disabled={create.isPending}
                    />
                    <span>{code}</span>
                  </label>
                ))}
              </div>
            ) : null}
          </fieldset>
          <div className="row" style={{ gap: 8 }}>
            <button
              type="submit"
              className="btn btn-primary btn-sm"
              disabled={
                create.isPending || !url.trim() || (!wildcard && selectedEvents.size === 0)
              }
            >
              {create.isPending ? t('webhooks.form.creating') : t('webhooks.form.create')}
            </button>
            <button type="button" className="btn btn-secondary btn-sm" onClick={resetCreate} disabled={create.isPending}>
              {t('webhooks.form.cancel')}
            </button>
          </div>
        </form>
      ) : null}

      {webhooksQuery.isLoading ? (
        <LoadingState label={t('webhooks.loading')} />
      ) : webhooksQuery.isError ? (
        <ErrorState message={errorMessage(webhooksQuery.error)} onRetry={() => webhooksQuery.refetch()} />
      ) : subscriptions.length === 0 ? (
        <p className="muted" style={{ marginTop: 8 }}>
          {t('webhooks.empty')}
        </p>
      ) : (
        <ul className="webhooks-list">
          {subscriptions.map((sub) => (
            <li key={sub.id} className="webhooks-list-row">
              <div className="webhook-row-main">
                <code className="webhook-url">{sub.url}</code>
                <span className={`badge ${sub.active ? 'badge-success' : 'badge-muted'}`}>
                  {sub.active ? t('webhooks.badge.active') : t('webhooks.badge.disabled')}
                </span>
                <span className="muted webhook-events">{sub.eventTypes.join(', ')}</span>
              </div>
              <div className="table-actions">
                <button type="button" className="btn btn-secondary btn-sm" onClick={() => toggleActive(sub)} disabled={update.isPending}>
                  {sub.active ? t('webhooks.actions.disable') : t('webhooks.actions.enable')}
                </button>
                <button type="button" className="btn btn-secondary btn-sm" onClick={() => sendPing(sub)} disabled={ping.isPending}>
                  {t('webhooks.actions.ping')}
                </button>
                <button type="button" className="btn btn-secondary btn-sm" onClick={() => rotateSecret(sub)} disabled={update.isPending}>
                  {t('webhooks.actions.rotateSecret')}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  aria-expanded={deliveriesFor === sub.id}
                  onClick={() => setDeliveriesFor((cur) => (cur === sub.id ? null : sub.id))}
                >
                  {t('webhooks.actions.deliveries')}
                </button>
                <button type="button" className="btn btn-danger btn-sm" onClick={() => setDeleteTarget(sub)}>
                  {t('webhooks.actions.delete')}
                </button>
              </div>
              {deliveriesFor === sub.id ? <DeliveriesPanel subscriptionId={sub.id} /> : null}
            </li>
          ))}
        </ul>
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        title={t('webhooks.deleteDialog.title')}
        message={
          <Trans t={t} i18nKey="webhooks.deleteDialog.message" values={{ url: deleteTarget?.url ?? '' }}>
            Delete the webhook to <strong>{deleteTarget?.url}</strong>? Its delivery history will be
            removed too. This cannot be undone.
          </Trans>
        }
        confirmLabel={t('webhooks.deleteDialog.confirmLabel')}
        danger
        busy={remove.isPending}
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

// Recent deliveries for a subscription: status, attempts, last error (§10.4).
function DeliveriesPanel({ subscriptionId }: { subscriptionId: string }) {
  const { t } = useTranslation('integrations');
  const query = useWebhookDeliveries(subscriptionId, true);
  const items = query.data?.items ?? [];

  if (query.isLoading) return <LoadingState label={t('webhooks.deliveries.loading')} />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => query.refetch()} />;
  if (items.length === 0) return <p className="muted webhook-deliveries-empty">{t('webhooks.deliveries.empty')}</p>;

  return (
    <table className="data-table webhook-deliveries-table">
      <thead>
        <tr>
          <th>{t('webhooks.deliveries.event')}</th>
          <th>{t('webhooks.deliveries.status')}</th>
          <th>{t('webhooks.deliveries.attempts')}</th>
          <th>{t('webhooks.deliveries.lastResult')}</th>
          <th>{t('webhooks.deliveries.created')}</th>
        </tr>
      </thead>
      <tbody>
        {items.map((d) => (
          <tr key={d.id}>
            <td>{d.eventType}</td>
            <td>
              <span
                className={`badge ${
                  d.status === 'delivered' ? 'badge-success' : d.status === 'failed' ? 'badge-danger' : 'badge-muted'
                }`}
              >
                {d.status}
              </span>
            </td>
            <td>{d.attempts}</td>
            <td className="muted">
              {d.lastError
                ? d.lastError
                : d.lastStatusCode
                  ? t('webhooks.deliveries.httpStatus', { code: d.lastStatusCode })
                  : '—'}
            </td>
            <td className="nowrap muted">{formatUtc(d.createdAt)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
