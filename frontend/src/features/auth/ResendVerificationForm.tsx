// Resend-verification form. Reused from both the login screen and the
// verification-result screen (US-AUTH-3). The API response is intentionally
// non-committal (anti-enumeration, A8), so we always show the same neutral
// success message regardless of whether the account exists/needs verification.

import { useState, type FormEvent } from 'react';
import { authApi } from '@/api/endpoints';
import { errorMessage } from '@/lib/errors';

export function ResendVerificationForm({
  initialEmail = '',
  onDone,
}: {
  initialEmail?: string;
  onDone?: () => void;
}) {
  const [email, setEmail] = useState(initialEmail);
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setMessage(null);
    if (!email.trim()) {
      setError('Please enter your email address.');
      return;
    }
    setSubmitting(true);
    try {
      const res = await authApi.resendVerification({ email: email.trim() });
      // Server returns the neutral message; show it (or a sensible default).
      setMessage(
        res?.message ?? 'If an account needs verification, a new email has been sent.',
      );
      onDone?.();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} noValidate>
      {message ? <div className="banner banner-success">{message}</div> : null}
      {error ? <div className="banner banner-error">{error}</div> : null}
      <div className="field">
        <label htmlFor="resend-email">Email</label>
        <input
          id="resend-email"
          className="input"
          type="email"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          disabled={submitting}
        />
      </div>
      <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={submitting}>
        {submitting ? 'Sending…' : 'Resend verification email'}
      </button>
    </form>
  );
}
