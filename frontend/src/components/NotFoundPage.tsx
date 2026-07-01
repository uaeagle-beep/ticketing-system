import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

export function NotFoundPage() {
  const { t } = useTranslation('common');
  return (
    <div className="auth-shell">
      <div className="auth-card" style={{ textAlign: 'center' }}>
        <h1 style={{ fontSize: 22, marginBottom: 8 }}>{t('notFound.title')}</h1>
        <p className="muted" style={{ marginBottom: 16 }}>
          {t('notFound.body')}
        </p>
        <Link to="/board" className="btn btn-primary">
          {t('notFound.goToBoard')}
        </Link>
      </div>
    </div>
  );
}
