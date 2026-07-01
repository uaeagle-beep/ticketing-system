// Forgot-password (request) screen (F-01). Public route. Collects an email and POSTs to
// /api/auth/forgot-password. The response is intentionally non-committal (anti-enumeration, §6.1),
// so we always show the same neutral success message regardless of whether the account exists.

import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { authApi } from '@/api/endpoints';
import { errorMessage } from '@/lib/errors';

export function ForgotPasswordPage() {
  const { t } = useTranslation('auth');
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setMessage(null);
    if (!email.trim()) {
      setError(t('forgot.emptyEmail'));
      return;
    }
    setSubmitting(true);
    try {
      const res = await authApi.forgotPassword({ email: email.trim() });
      setMessage(res?.message ?? t('forgot.defaultSuccess'));
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-shell">
      <div className="auth-card">
        <div className="auth-brand">{t('brand')}</div>
        <h1 className="auth-title">{t('forgot.title')}</h1>

        {message ? (
          <>
            <div className="banner banner-success">{message}</div>
            <div className="auth-footer">
              <Link to="/login">{t('forgot.backToLogin')}</Link>
            </div>
          </>
        ) : (
          <>
            <p className="muted" style={{ marginBottom: 16 }}>
              {t('forgot.intro')}
            </p>
            {error ? <div className="banner banner-error">{error}</div> : null}
            <form onSubmit={handleSubmit} noValidate>
              <div className="field">
                <label htmlFor="forgot-email">{t('email')}</label>
                <input
                  id="forgot-email"
                  className="input"
                  type="email"
                  autoComplete="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  disabled={submitting}
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%' }}
                disabled={submitting}
              >
                {submitting ? t('forgot.submitting') : t('forgot.submit')}
              </button>
            </form>
            <div className="auth-footer">
              <Link to="/login">{t('forgot.backToLogin')}</Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
