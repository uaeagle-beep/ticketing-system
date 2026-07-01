// Language switcher (Wave 3 i18n, ADR-0022). Lives in the AppLayout header. Changing the language:
//   1. updates the active i18next language (instant UI re-render),
//   2. persists the choice to localStorage (authoritative for the UI, offline/instant),
//   3. mirrors it to the profile via PUT /api/me/profile { name, locale } when logged in, so the
//      choice follows the user across devices (the current display name is preserved on the call).
// The profile mirror is best-effort: a failed API call does NOT roll back the local UI choice.

import { useTranslation } from 'react-i18next';
import { setLanguage, currentLanguage } from '@/i18n/config';
import { SUPPORTED_LANGUAGES } from '@/i18n/resources';
import { meApi } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthContext';

export function LanguageSwitcher() {
  const { t } = useTranslation('common');
  const { status, user, updateUser } = useAuth();
  const active = currentLanguage();

  const onChange = async (lang: string) => {
    if (lang === active) return;
    // 1 + 2: switch + persist locally (this is what the UI trusts).
    await setLanguage(lang);
    // 3: mirror to the profile when authenticated (preserve the display name).
    if (status === 'authenticated' && user) {
      try {
        const updated = await meApi.updateProfile({ name: user.name, locale: lang });
        updateUser(updated);
      } catch {
        // Best-effort: the local choice (localStorage) still holds; the next login re-syncs.
      }
    }
  };

  return (
    <div className="lang-switcher" role="group" aria-label={t('language.label')}>
      <label htmlFor="lang-select" className="sr-only">
        {t('language.label')}
      </label>
      <select
        id="lang-select"
        className="select select-sm"
        aria-label={t('language.label')}
        value={active}
        onChange={(e) => void onChange(e.target.value)}
      >
        {SUPPORTED_LANGUAGES.map((lng) => (
          <option key={lng} value={lng}>
            {t(`language.name.${lng}`)}
          </option>
        ))}
      </select>
    </div>
  );
}
