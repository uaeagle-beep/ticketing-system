// Reset-password (consume) screen (F-01). Public route. The emailed link is
// `${FRONTEND_URL}/reset-password?token=<token>`; we read the token from the query string and, on
// submit, POST it with the new password to /api/auth/reset-password. States:
//   - missing token in URL -> prompt to request a new link
//   - form                 -> new password + confirm (confirm is client-only)
//   - success              -> "Continue to login"
// On 400 invalid_or_expired_token we show an error with a "request a new link" affordance.

import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { authApi } from '@/api/endpoints';
import { errorMessage } from '@/lib/errors';

const PASSWORD_MIN = 8;

export function ResetPasswordPage() {
  const { t } = useTranslation('auth');
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');

  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (password.length < PASSWORD_MIN) {
      setError(t('reset.passwordTooShort', { count: PASSWORD_MIN }));
      return;
    }
    if (password !== confirm) {
      setError(t('reset.passwordsDoNotMatch'));
      return;
    }
    if (!token) {
      setError(t('reset.invalidLink'));
      return;
    }
    setSubmitting(true);
    try {
      await authApi.resetPassword({ token, password });
      setDone(true);
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
        <h1 className="auth-title">{t('reset.title')}</h1>

        {done ? (
          <>
            <div className="banner banner-success">{t('reset.defaultSuccess')}</div>
            <Link to="/login" className="btn btn-primary" style={{ width: '100%' }}>
              {t('reset.continueToLogin')}
            </Link>
          </>
        ) : !token ? (
          <>
            <div className="banner banner-error">{t('reset.missingBanner')}</div>
            <Link to="/forgot-password" className="btn btn-primary" style={{ width: '100%' }}>
              {t('reset.requestNewLink')}
            </Link>
            <div className="auth-footer">
              <Link to="/login">{t('reset.backToLogin')}</Link>
            </div>
          </>
        ) : (
          <>
            {error ? <div className="banner banner-error">{error}</div> : null}
            <form onSubmit={handleSubmit} noValidate>
              <div className="field">
                <label htmlFor="reset-password">{t('reset.newPassword')}</label>
                <input
                  id="reset-password"
                  className="input"
                  type="password"
                  autoComplete="new-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  disabled={submitting}
                />
              </div>
              <div className="field">
                <label htmlFor="reset-confirm">{t('reset.confirmPassword')}</label>
                <input
                  id="reset-confirm"
                  className="input"
                  type="password"
                  autoComplete="new-password"
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                  disabled={submitting}
                />
              </div>
              <button
                type="submit"
                className="btn btn-primary"
                style={{ width: '100%' }}
                disabled={submitting}
              >
                {submitting ? t('reset.submitting') : t('reset.submit')}
              </button>
            </form>
            <div className="auth-footer">
              <Link to="/forgot-password">{t('reset.requestNewLinkArrow')}</Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
