// API-keys management section on the Account page (Wave 3, ADR-0021, §10.4). Self: the caller manages
// their own personal access tokens (ptk_…) for the public /api/v1 surface. List shows name, prefix,
// scopes, created / last-used, and revoked state. Create takes a name + scope checkboxes and reveals the
// raw key ONCE (copy-to-clipboard + "won't see it again" warning). Revoke is confirmed. Errors → toasts.

import { useState, type FormEvent } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { API_KEY_SCOPES, type ApiKey } from '@/api/types';
import { useApiKeys, useApiKeyMutations } from './useApiKeys';
import { SecretReveal } from '@/features/integrations/SecretReveal';
import { errorMessage } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { LoadingState, ErrorState } from '@/components/States';
import { formatUtc } from '@/lib/time';

export function ApiKeysManager() {
  const { t } = useTranslation('account');
  const toast = useToast();
  const keysQuery = useApiKeys();
  const { create, revoke } = useApiKeyMutations();

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState('');
  const [selectedScopes, setSelectedScopes] = useState<Set<string>>(new Set(['tickets:read']));
  const [revealedKey, setRevealedKey] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<ApiKey | null>(null);

  const toggleScope = (scope: string) => {
    setSelectedScopes((prev) => {
      const next = new Set(prev);
      if (next.has(scope)) next.delete(scope);
      else next.add(scope);
      return next;
    });
  };

  const resetCreate = () => {
    setName('');
    setSelectedScopes(new Set(['tickets:read']));
    setShowCreate(false);
  };

  const submitCreate = (e: FormEvent) => {
    e.preventDefault();
    const scopes = Array.from(selectedScopes);
    if (!name.trim() || scopes.length === 0) return;
    create.mutate(
      { name: name.trim(), scopes },
      {
        onSuccess: (res) => {
          toast.showSuccess(t('apiKeys.created'));
          setRevealedKey(res.secret);
          resetCreate();
        },
        onError: (err) => toast.showError(errorMessage(err)),
      },
    );
  };

  const confirmRevoke = () => {
    if (!revokeTarget) return;
    revoke.mutate(revokeTarget.id, {
      onSuccess: () => {
        toast.showSuccess(t('apiKeys.revoked'));
        setRevokeTarget(null);
      },
      onError: (err) => {
        setRevokeTarget(null);
        toast.showError(errorMessage(err));
      },
    });
  };

  const keys = keysQuery.data ?? [];

  return (
    <section className="panel" style={{ marginTop: 20 }}>
      <div className="row" style={{ alignItems: 'center', marginBottom: 12 }}>
        <h2 style={{ fontSize: 16, margin: 0 }}>{t('apiKeys.heading')}</h2>
        <div className="spacer" />
        <button type="button" className="btn btn-primary btn-sm" onClick={() => setShowCreate((v) => !v)}>
          {t('apiKeys.newKey')}
        </button>
      </div>
      <p className="muted" style={{ marginBottom: 12 }}>
        <Trans t={t} i18nKey="apiKeys.description">
          Personal access tokens for the public API (<code>/api/v1</code>). Send as{' '}
          <code>Authorization: Bearer ptk_…</code>. Keys are scoped and can never delete data or reach admin.
        </Trans>
      </p>

      {revealedKey ? (
        <div style={{ marginBottom: 12 }}>
          <SecretReveal secret={revealedKey} label={t('apiKeys.label')} />
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            style={{ marginTop: 6 }}
            onClick={() => setRevealedKey(null)}
          >
            {t('apiKeys.done')}
          </button>
        </div>
      ) : null}

      {showCreate ? (
        <form className="api-key-create-form" onSubmit={submitCreate} aria-label={t('apiKeys.form.ariaLabel')}>
          <div className="field">
            <label htmlFor="api-key-name">{t('apiKeys.form.name')}</label>
            <input
              id="api-key-name"
              className="input"
              placeholder={t('apiKeys.form.namePlaceholder')}
              value={name}
              maxLength={100}
              onChange={(e) => setName(e.target.value)}
              disabled={create.isPending}
            />
          </div>
          <fieldset className="field" style={{ border: 0, padding: 0, margin: 0 }}>
            <legend>{t('apiKeys.form.scopes')}</legend>
            {API_KEY_SCOPES.map((scope) => (
              <label key={scope} className="row" style={{ gap: 6, alignItems: 'center' }}>
                <input
                  type="checkbox"
                  checked={selectedScopes.has(scope)}
                  onChange={() => toggleScope(scope)}
                  disabled={create.isPending}
                />
                <span>{scope}</span>
              </label>
            ))}
            <p className="muted" style={{ margin: '4px 0 0', fontSize: 12 }}>
              <code>tickets:write</code> {t('apiKeys.form.writeIncludesRead')}
            </p>
          </fieldset>
          <div className="row" style={{ gap: 8 }}>
            <button
              type="submit"
              className="btn btn-primary btn-sm"
              disabled={create.isPending || !name.trim() || selectedScopes.size === 0}
            >
              {create.isPending ? t('apiKeys.form.creating') : t('apiKeys.form.create')}
            </button>
            <button type="button" className="btn btn-secondary btn-sm" onClick={resetCreate} disabled={create.isPending}>
              {t('apiKeys.form.cancel')}
            </button>
          </div>
        </form>
      ) : null}

      {keysQuery.isLoading ? (
        <LoadingState label={t('apiKeys.loading')} />
      ) : keysQuery.isError ? (
        <ErrorState message={errorMessage(keysQuery.error)} onRetry={() => keysQuery.refetch()} />
      ) : keys.length === 0 ? (
        <p className="muted" style={{ marginTop: 8 }}>
          {t('apiKeys.empty')}
        </p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>{t('apiKeys.table.name')}</th>
              <th>{t('apiKeys.table.prefix')}</th>
              <th>{t('apiKeys.table.scopes')}</th>
              <th>{t('apiKeys.table.lastUsed')}</th>
              <th>{t('apiKeys.table.status')}</th>
              <th className="text-right">{t('apiKeys.table.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {keys.map((key) => (
              <tr key={key.id}>
                <td>{key.name}</td>
                <td>
                  <code>{key.prefix}…</code>
                </td>
                <td className="muted">{key.scopes.join(', ')}</td>
                <td className="nowrap muted">{key.lastUsedAt ? formatUtc(key.lastUsedAt) : t('apiKeys.table.never')}</td>
                <td>
                  {key.revokedAt ? (
                    <span className="badge badge-muted">{t('apiKeys.table.revoked')}</span>
                  ) : (
                    <span className="badge badge-success">{t('apiKeys.table.active')}</span>
                  )}
                </td>
                <td>
                  <div className="table-actions">
                    {key.revokedAt ? null : (
                      <button type="button" className="btn btn-danger btn-sm" onClick={() => setRevokeTarget(key)}>
                        {t('apiKeys.table.revoke')}
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <ConfirmDialog
        open={revokeTarget !== null}
        title={t('apiKeys.revokeDialog.title')}
        message={
          <Trans t={t} i18nKey="apiKeys.revokeDialog.message" values={{ name: revokeTarget?.name ?? '' }}>
            Revoke <strong>{revokeTarget?.name}</strong>? Any client using it will stop working immediately.
            This cannot be undone.
          </Trans>
        }
        confirmLabel={t('apiKeys.revokeDialog.confirmLabel')}
        danger
        busy={revoke.isPending}
        onConfirm={confirmRevoke}
        onCancel={() => setRevokeTarget(null)}
      />
    </section>
  );
}
