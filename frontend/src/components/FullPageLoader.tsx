import { useTranslation } from 'react-i18next';

export function FullPageLoader({ label }: { label?: string }) {
  const { t } = useTranslation('common');
  return (
    <div className="full-loader" role="status" aria-live="polite">
      <div className="spinner" aria-hidden />
      <span>{label ?? t('states.loading')}</span>
    </div>
  );
}
