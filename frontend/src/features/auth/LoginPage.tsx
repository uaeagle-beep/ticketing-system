// Login screen (Wireframe 2). Email + password, link to resend verification
// when the account is not verified, and a link to create an account.

import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { authApi } from '@/api/endpoints';
import { ApiError } from '@/api/client';
import { errorMessage } from '@/lib/errors';
import { useAuth } from '@/auth/AuthContext';
import { ResendVerificationForm } from './ResendVerificationForm';

interface LocationState {
  from?: { pathname?: string };
}

export function LoginPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const redirectTo = (location.state as LocationState | null)?.from?.pathname ?? '/board';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // When the account is unverified we surface an inline resend affordance.
  const [showResend, setShowResend] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setShowResend(false);

    if (!email.trim() || !password) {
      setError('Please enter your email and password.');
      return;
    }

    setSubmitting(true);
    try {
      const res = await authApi.login({ email: email.trim(), password });
      signIn(res);
      navigate(redirectTo, { replace: true });
    } catch (err) {
      if (err instanceof ApiError && err.code === 'account_not_verified') {
        // Scoped unverified hint (A4): offer the resend action.
        setShowResend(true);
      }
      setError(errorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="auth-shell">
      <div className="auth-card">
        <div className="auth-brand">Ticket Tracker</div>
        <h1 className="auth-title">Log in</h1>

        {error ? <div className="banner banner-error">{error}</div> : null}

        <form onSubmit={handleSubmit} noValidate>
          <div className="field">
            <label htmlFor="login-email">Email</label>
            <input
              id="login-email"
              className="input"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={submitting}
            />
          </div>
          <div className="field">
            <label htmlFor="login-password">Password</label>
            <input
              id="login-password"
              className="input"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={submitting}
            />
          </div>
          <button
            type="submit"
            className="btn btn-primary"
            style={{ width: '100%' }}
            disabled={submitting}
          >
            {submitting ? 'Logging in…' : 'Log in'}
          </button>
        </form>

        {showResend ? (
          <div style={{ marginTop: 18 }}>
            <div className="banner banner-info">
              Your account is not verified. Request a new verification email below.
            </div>
            <ResendVerificationForm initialEmail={email.trim()} />
          </div>
        ) : null}

        <div className="auth-footer">
          {!showResend ? (
            <button
              type="button"
              className="btn-link"
              onClick={() => setShowResend(true)}
            >
              Account not verified? Resend email
            </button>
          ) : null}
          <div>
            <Link to="/forgot-password">Forgot password?</Link>
          </div>
          <div>
            <Link to="/signup">Create an account →</Link>
          </div>
        </div>
      </div>
    </div>
  );
}
