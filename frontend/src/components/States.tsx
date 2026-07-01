// Reusable loading / empty / error state blocks (NFR-USE-1).

import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

export function LoadingState({ label }: { label?: string }) {
  const { t } = useTranslation('common');
  return (
    <div className="center-state" role="status" aria-live="polite">
      <div className="spinner" aria-hidden />
      <span>{label ?? t('states.loading')}</span>
    </div>
  );
}

export function EmptyState({
  title,
  message,
  action,
}: {
  title: string;
  message?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="center-state">
      <h3>{title}</h3>
      {message ? <p className="muted">{message}</p> : null}
      {action}
    </div>
  );
}

export function ErrorState({
  message,
  onRetry,
}: {
  message: string;
  onRetry?: () => void;
}) {
  const { t } = useTranslation('common');
  return (
    <div className="center-state" role="alert">
      <h3>{t('states.errorTitle')}</h3>
      <p className="muted">{message}</p>
      {onRetry ? (
        <button type="button" className="btn btn-secondary" onClick={onRetry}>
          {t('states.tryAgain')}
        </button>
      ) : null}
    </div>
  );
}
