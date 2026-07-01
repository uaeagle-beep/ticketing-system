// Shows a server-generated password ONCE with a Copy button ([ПРИПУЩЕННЯ UM-5]). The password is
// never fetched again — the admin must copy it now and share it out-of-band.

import { useState } from 'react';
import { useTranslation } from 'react-i18next';

interface GeneratedPasswordNoticeProps {
  password: string;
}

export function GeneratedPasswordNotice({ password }: GeneratedPasswordNoticeProps) {
  const { t } = useTranslation('users');
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(password);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard may be unavailable (e.g. insecure context); the value stays visible to copy manually.
      setCopied(false);
    }
  };

  return (
    <div className="banner banner-info generated-password">
      <p style={{ margin: '0 0 6px' }}>{t('generatedPassword.warning')}</p>
      <div className="row" style={{ gap: 8, alignItems: 'center' }}>
        <code className="generated-password-value" data-testid="generated-password">
          {password}
        </code>
        <button type="button" className="btn btn-secondary btn-sm" onClick={copy}>
          {copied ? t('generatedPassword.copied') : t('generatedPassword.copy')}
        </button>
      </div>
    </div>
  );
}
