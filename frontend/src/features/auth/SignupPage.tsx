// Sign-up screen (Wireframe 2). Email, password (min 8), confirm password.
// Confirm-password is a client-only UX guard. On success the API returns a
// "verification required" message and issues NO session (no auto-login).

import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { authApi } from '@/api/endpoints';
import { errorMessage } from '@/lib/errors';

const MIN_PASSWORD = 8;

export function SignupPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<{ password?: string; confirm?: string }>({});

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    const nextFieldErrors: typeof fieldErrors = {};
    if (password.length < MIN_PASSWORD) {
      nextFieldErrors.password = `Password must be at least ${MIN_PASSWORD} characters.`;
    }
    if (password !== confirm) {
      nextFieldErrors.confirm = 'Passwords do not match.';
    }
    setFieldErrors(nextFieldErrors);
    if (Object.keys(nextFieldErrors).length > 0) return;

    setSubmitting(true);
    try {
      // Confirm-password is NOT sent to the API (client-only guard).
      const res = await authApi.signup({ email: email.trim(), password });
      setSuccess(
        res?.message ??
          'Account created. Please check your email to verify your account before logging in.',
      );
      setEmail('');
      setPassword('');
      setConfirm('');
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
        <h1 className="auth-title">Create an account</h1>

        {error ? <div className="banner banner-error">{error}</div> : null}
        {success ? (
          <div className="banner banner-success">
            {success}
            <div style={{ marginTop: 8 }}>
              <Link to="/login">Continue to login →</Link>
            </div>
          </div>
        ) : null}

        {!success ? (
          <form onSubmit={handleSubmit} noValidate>
            <div className="field">
              <label htmlFor="signup-email">Email</label>
              <input
                id="signup-email"
                className="input"
                type="email"
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={submitting}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="signup-password">Password</label>
              <input
                id="signup-password"
                className="input"
                type="password"
                autoComplete="new-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={submitting}
                aria-describedby="signup-password-hint"
              />
              <span id="signup-password-hint" className="field-hint">
                Minimum {MIN_PASSWORD} characters.
              </span>
              {fieldErrors.password ? (
                <span className="field-error">{fieldErrors.password}</span>
              ) : null}
            </div>
            <div className="field">
              <label htmlFor="signup-confirm">Confirm password</label>
              <input
                id="signup-confirm"
                className="input"
                type="password"
                autoComplete="new-password"
                value={confirm}
                onChange={(e) => setConfirm(e.target.value)}
                disabled={submitting}
              />
              {fieldErrors.confirm ? (
                <span className="field-error">{fieldErrors.confirm}</span>
              ) : null}
            </div>
            <button
              type="submit"
              className="btn btn-primary"
              style={{ width: '100%' }}
              disabled={submitting}
            >
              {submitting ? 'Signing up…' : 'Sign up'}
            </button>
            <p className="field-hint" style={{ marginTop: 12, textAlign: 'center' }}>
              Email verification is required.
            </p>
          </form>
        ) : null}

        <div className="auth-footer">
          <div>
            Already registered? <Link to="/login">Log in →</Link>
          </div>
        </div>
      </div>
    </div>
  );
}
