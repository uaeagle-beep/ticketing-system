// Shows a one-time secret (a webhook signing secret or an API key) with a Copy button and a "shown only
// once" warning (Wave 3, ADR-0021). Mirrors the user-management GeneratedPasswordNotice pattern: the value
// is never fetched again, so the user must copy it now. Reused by the webhooks + API-keys management UIs.

import { useState } from 'react';
import { useTranslation } from 'react-i18next';

export function SecretReveal({ secret, label }: { secret: string; label: string }) {
  const { t } = useTranslation('integrations');
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard may be unavailable (insecure context); the value stays visible to copy manually.
      setCopied(false);
    }
  };

  return (
    <div className="banner banner-info generated-password">
      <p style={{ margin: '0 0 6px' }}>
        {t('secretReveal.notice', { label })}
      </p>
      <div className="row" style={{ gap: 8, alignItems: 'center' }}>
        <code className="generated-password-value" data-testid="revealed-secret">
          {secret}
        </code>
        <button type="button" className="btn btn-secondary btn-sm" onClick={copy}>
          {copied ? t('secretReveal.copied') : t('secretReveal.copy')}
        </button>
      </div>
    </div>
  );
}
