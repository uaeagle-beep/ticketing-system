// Email verification result screen (Wireframe 2).
//
// The emailed link is `${FRONTEND_URL}/verify-email?token=<token>`. We read the
// token from the query string and POST it to /api/auth/verify-email (the token
// is never retained in app navigation beyond this call). States:
//   - verifying  -> spinner
//   - success    -> "Email verified" + "Continue to login" (no auto-login)
//   - error      -> invalid/expired message + resend action
//   - missing    -> no token in URL; prompt to resend / go to login

import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { authApi } from '@/api/endpoints';
import { errorMessage } from '@/lib/errors';
import { ResendVerificationForm } from './ResendVerificationForm';

type VerifyStatus = 'verifying' | 'success' | 'error' | 'missing';

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');

  const [status, setStatus] = useState<VerifyStatus>(token ? 'verifying' : 'missing');
  const [message, setMessage] = useState<string>('');
  const [showResend, setShowResend] = useState(false);
  // Guard against double-invocation (React 18 StrictMode) consuming the token twice.
  const attempted = useRef(false);

  useEffect(() => {
    if (!token) {
      setStatus('missing');
      return;
    }
    if (attempted.current) return;
    attempted.current = true;

    authApi
      .verifyEmail({ token })
      .then((res) => {
        setStatus('success');
        setMessage(res?.message ?? 'Email verified — your account is ready to use.');
      })
      .catch((err) => {
        setStatus('error');
        setMessage(errorMessage(err));
      });
  }, [token]);

  return (
    <div className="auth-shell">
      <div className="auth-card">
        <div className="auth-brand">Ticket Tracker</div>
        <h1 className="auth-title">Email verification</h1>

        {status === 'verifying' ? (
          <div className="center-state" style={{ padding: '24px 0' }}>
            <div className="spinner" aria-hidden />
            <span className="muted">Verifying your email…</span>
          </div>
        ) : null}

        {status === 'success' ? (
          <>
            <div className="banner banner-success">
              {message || 'Email verified — your account is ready to use.'}
            </div>
            <Link to="/login" className="btn btn-primary" style={{ width: '100%' }}>
              Continue to login
            </Link>
          </>
        ) : null}

        {status === 'error' ? (
          <>
            <div className="banner banner-error">{message}</div>
            {showResend ? (
              <ResendVerificationForm />
            ) : (
              <button
                type="button"
                className="btn btn-primary"
                style={{ width: '100%' }}
                onClick={() => setShowResend(true)}
              >
                Resend verification email
              </button>
            )}
            <div className="auth-footer">
              <Link to="/login">Back to login →</Link>
            </div>
          </>
        ) : null}

        {status === 'missing' ? (
          <>
            <div className="banner banner-info">
              No verification token was found in this link. You can request a new
              verification email below.
            </div>
            <ResendVerificationForm />
            <div className="auth-footer">
              <Link to="/login">Back to login →</Link>
            </div>
          </>
        ) : null}
      </div>
    </div>
  );
}
