// Reset-password (consume) screen (F-01). Public route. The emailed link is
// `${FRONTEND_URL}/reset-password?token=<token>`; we read the token from the query string and, on
// submit, POST it with the new password to /api/auth/reset-password. States:
//   - missing token in URL -> prompt to request a new link
//   - form                 -> new password + confirm (confirm is client-only)
//   - success              -> "Continue to login"
// On 400 invalid_or_expired_token we show an error with a "request a new link" affordance.

import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { authApi } from '@/api/endpoints';
import { errorMessage } from '@/lib/errors';

const PASSWORD_MIN = 8;

export function ResetPasswordPage() {
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
      setError(`Password must be at least ${PASSWORD_MIN} characters.`);
      return;
    }
    if (password !== confirm) {
      setError('The passwords do not match.');
      return;
    }
    if (!token) {
      setError('This reset link is invalid or has expired. Request a new one.');
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
        <div className="auth-brand">Ticket Tracker</div>
        <h1 className="auth-title">Choose a new password</h1>

        {done ? (
          <>
            <div className="banner banner-success">
              Your password has been reset. Please log in with your new password.
            </div>
            <Link to="/login" className="btn btn-primary" style={{ width: '100%' }}>
              Continue to login
            </Link>
          </>
        ) : !token ? (
          <>
            <div className="banner banner-error">
              No reset token was found in this link. Request a new password reset below.
            </div>
            <Link to="/forgot-password" className="btn btn-primary" style={{ width: '100%' }}>
              Request a new link
            </Link>
            <div className="auth-footer">
              <Link to="/login">Back to login →</Link>
            </div>
          </>
        ) : (
          <>
            {error ? <div className="banner banner-error">{error}</div> : null}
            <form onSubmit={handleSubmit} noValidate>
              <div className="field">
                <label htmlFor="reset-password">New password</label>
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
                <label htmlFor="reset-confirm">Confirm new password</label>
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
                {submitting ? 'Resetting…' : 'Reset password'}
              </button>
            </form>
            <div className="auth-footer">
              <Link to="/forgot-password">Request a new link →</Link>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
