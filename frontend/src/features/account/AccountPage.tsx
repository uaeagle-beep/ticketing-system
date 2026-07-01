// Account / Profile page (F-04, authenticated). Two self-service sections:
//  - Display name: PUT /api/me/profile (set/clear); updates the cached identity from the returned user.
//  - Change password: POST /api/me/password (current-password re-auth). On success the current session
//    stays valid and all other devices are signed out; a wrong current password maps to a field error.

import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { meApi } from '@/api/endpoints';
import type { AuthUser } from '@/api/types';
import { useAuth } from '@/auth/AuthContext';
import { queryKeys } from '@/lib/queryKeys';
import { errorMessage, isApiErrorCode } from '@/lib/errors';
import { useToast } from '@/components/toast/ToastContext';
import { ApiKeysManager } from './ApiKeysManager';

const NAME_MAX = 100;
const PASSWORD_MIN = 8;

export function AccountPage() {
  const { t } = useTranslation('account');
  const { user, updateUser } = useAuth();
  const toast = useToast();

  // ---- Display name ----
  const [name, setName] = useState(user?.name ?? '');
  const [nameError, setNameError] = useState<string | null>(null);

  const profileMutation = useMutation({
    mutationFn: (payload: { name: string | null }) => meApi.updateProfile(payload),
    onSuccess: (updated: AuthUser) => {
      updateUser(updated);
      setName(updated.name ?? '');
      toast.showSuccess(t('profile.updated'));
    },
    onError: (err) => {
      if (isApiErrorCode(err, 'validation_error')) setNameError(errorMessage(err));
      else toast.showError(errorMessage(err));
    },
  });

  const submitName = (e: FormEvent) => {
    e.preventDefault();
    setNameError(null);
    const trimmed = name.trim();
    if (trimmed.length > NAME_MAX) {
      setNameError(t('profile.nameTooLong', { count: NAME_MAX }));
      return;
    }
    // Blank clears the name (server stores null).
    profileMutation.mutate({ name: trimmed === '' ? null : trimmed });
  };

  // ---- Change password ----
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [pwError, setPwError] = useState<string | null>(null);
  const [currentPwError, setCurrentPwError] = useState<string | null>(null);

  const passwordMutation = useMutation({
    mutationFn: (payload: { currentPassword: string; newPassword: string }) =>
      meApi.changePassword(payload),
    onSuccess: () => {
      setCurrentPassword('');
      setNewPassword('');
      setConfirm('');
      toast.showSuccess(t('password.changed'));
    },
    onError: (err) => {
      // Wrong current password re-auth => 401 invalid_credentials, shown as a field error.
      if (isApiErrorCode(err, 'invalid_credentials')) {
        setCurrentPwError(t('password.currentIncorrect'));
      } else if (isApiErrorCode(err, 'validation_error')) {
        setPwError(errorMessage(err));
      } else {
        toast.showError(errorMessage(err));
      }
    },
  });

  // ---- Notification settings (email toggle, Wave 2 §6.8) ----
  const queryClient = useQueryClient();
  const settingsQuery = useQuery({
    queryKey: queryKeys.notificationSettings,
    queryFn: ({ signal }) => meApi.getNotificationSettings(signal),
  });

  const settingsMutation = useMutation({
    mutationFn: (enabled: boolean) =>
      meApi.updateNotificationSettings({ emailNotificationsEnabled: enabled }),
    onSuccess: (updated) => {
      queryClient.setQueryData(queryKeys.notificationSettings, updated);
      toast.showSuccess(
        updated.emailNotificationsEnabled
          ? t('notifications.turnedOn')
          : t('notifications.turnedOff'),
      );
    },
    onError: (err) => toast.showError(errorMessage(err)),
  });

  const emailEnabled = settingsQuery.data?.emailNotificationsEnabled ?? true;

  const submitPassword = (e: FormEvent) => {
    e.preventDefault();
    setPwError(null);
    setCurrentPwError(null);
    if (!currentPassword) {
      setCurrentPwError(t('password.enterCurrent'));
      return;
    }
    if (newPassword.length < PASSWORD_MIN) {
      setPwError(t('password.tooShort', { count: PASSWORD_MIN }));
      return;
    }
    if (newPassword !== confirm) {
      setPwError(t('password.doNotMatch'));
      return;
    }
    passwordMutation.mutate({ currentPassword, newPassword });
  };

  return (
    <div className="account-page">
      <h1 style={{ fontSize: 20, marginBottom: 16 }}>{t('title')}</h1>

      <section className="panel" style={{ marginBottom: 20 }}>
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>{t('profile.heading')}</h2>
        <form onSubmit={submitName} noValidate>
          <div className="field">
            <label htmlFor="account-email">{t('profile.email')}</label>
            <input id="account-email" className="input" value={user?.email ?? ''} disabled readOnly />
          </div>
          <div className="field">
            <label htmlFor="account-name">{t('profile.displayName')}</label>
            <input
              id="account-name"
              className="input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('profile.displayNamePlaceholder')}
              maxLength={NAME_MAX}
              disabled={profileMutation.isPending}
            />
            {nameError ? <span className="field-error">{nameError}</span> : null}
          </div>
          <div className="row" style={{ justifyContent: 'flex-end' }}>
            <button type="submit" className="btn btn-primary" disabled={profileMutation.isPending}>
              {profileMutation.isPending ? t('profile.saving') : t('profile.save')}
            </button>
          </div>
        </form>
      </section>

      <section className="panel">
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>{t('password.heading')}</h2>
        <form onSubmit={submitPassword} noValidate>
          <div className="field">
            <label htmlFor="account-current-password">{t('password.current')}</label>
            <input
              id="account-current-password"
              className="input"
              type="password"
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              disabled={passwordMutation.isPending}
            />
            {currentPwError ? <span className="field-error">{currentPwError}</span> : null}
          </div>
          <div className="field">
            <label htmlFor="account-new-password">{t('password.new')}</label>
            <input
              id="account-new-password"
              className="input"
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              disabled={passwordMutation.isPending}
            />
          </div>
          <div className="field">
            <label htmlFor="account-confirm-password">{t('password.confirm')}</label>
            <input
              id="account-confirm-password"
              className="input"
              type="password"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              disabled={passwordMutation.isPending}
            />
            {pwError ? <span className="field-error">{pwError}</span> : null}
          </div>
          <div className="row" style={{ justifyContent: 'flex-end' }}>
            <button type="submit" className="btn btn-primary" disabled={passwordMutation.isPending}>
              {passwordMutation.isPending ? t('password.changing') : t('password.change')}
            </button>
          </div>
        </form>
      </section>

      <section className="panel" style={{ marginTop: 20 }}>
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>{t('notifications.heading')}</h2>
        <p className="muted" style={{ marginBottom: 12 }}>
          {t('notifications.description')}
        </p>
        <label className="row" style={{ gap: 8, alignItems: 'center' }}>
          <input
            type="checkbox"
            checked={emailEnabled}
            onChange={(e) => settingsMutation.mutate(e.target.checked)}
            disabled={settingsQuery.isLoading || settingsMutation.isPending}
          />
          <span>{t('notifications.emailDigests')}</span>
        </label>
      </section>

      <ApiKeysManager />
    </div>
  );
}
