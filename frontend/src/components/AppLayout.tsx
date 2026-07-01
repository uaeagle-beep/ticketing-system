// Authenticated app shell: header with brand, nav (Board / Teams / Epics),
// and a collapsed user menu showing the email + Log out (Wireframe 1).

import { useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/auth/AuthContext';
import { displayName } from '@/lib/displayName';
import { useUnreadCount } from '@/features/notifications/useNotifications';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';

export function AppLayout() {
  const { t } = useTranslation('common');
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const unreadQuery = useUnreadCount();
  const unread = unreadQuery.data?.unreadCount ?? 0;
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const onClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [menuOpen]);

  const handleLogout = async () => {
    setMenuOpen(false);
    await signOut();
    navigate('/login', { replace: true });
  };

  // Show the display name (name when set, else email) in the header; email stays the account key.
  const shown = user ? displayName(user.name, user.email) : '';
  const initial = shown.charAt(0).toUpperCase() || '?';

  return (
    <div className="app-shell">
      <header className="app-header">
        <NavLink to="/board" className="app-brand">
          {t('brand')}
        </NavLink>
        <nav className="app-nav">
          <NavLink to="/board" className={({ isActive }) => (isActive ? 'active' : '')}>
            {t('nav.board')}
          </NavLink>
          <NavLink to="/teams" className={({ isActive }) => (isActive ? 'active' : '')}>
            {t('nav.teams')}
          </NavLink>
          <NavLink to="/epics" className={({ isActive }) => (isActive ? 'active' : '')}>
            {t('nav.epics')}
          </NavLink>
          {/* Analytics is visible to any member (their teams); admin can pick any team (ADR-0020). */}
          <NavLink to="/analytics" className={({ isActive }) => (isActive ? 'active' : '')}>
            {t('nav.analytics')}
          </NavLink>
          {/* Users zone is admin-only (ADR-0007). Hidden for members; route also guarded. */}
          {user?.isAdmin ? (
            <NavLink to="/users" className={({ isActive }) => (isActive ? 'active' : '')}>
              {t('nav.users')}
            </NavLink>
          ) : null}
        </nav>
        <div className="app-header-spacer" />
        <LanguageSwitcher />
        <button
          type="button"
          className="notif-bell"
          aria-label={
            unread > 0 ? t('bell.labelWithCount', { count: unread }) : t('bell.label')
          }
          title={t('bell.label')}
          onClick={() => navigate('/notifications')}
        >
          <span aria-hidden>🔔</span>
          {unread > 0 ? (
            <span className="notif-badge" aria-hidden>
              {unread > 99 ? '99+' : unread}
            </span>
          ) : null}
        </button>
        <div className="user-menu" ref={menuRef}>
          <button
            type="button"
            className="user-menu-trigger"
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((v) => !v)}
          >
            <span className="user-avatar" aria-hidden>
              {initial}
            </span>
            <span className="nowrap muted">{shown}</span>
          </button>
          {menuOpen ? (
            <div className="user-menu-popover" role="menu">
              {user?.name ? <div className="user-menu-name">{shown}</div> : null}
              <div className="user-menu-email">{user?.email}</div>
              <button
                type="button"
                className="btn btn-ghost"
                style={{ width: '100%', justifyContent: 'flex-start' }}
                onClick={() => {
                  setMenuOpen(false);
                  navigate('/account');
                }}
                role="menuitem"
              >
                {t('userMenu.account')}
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                style={{ width: '100%', justifyContent: 'flex-start' }}
                onClick={handleLogout}
                role="menuitem"
              >
                {t('userMenu.logOut')}
              </button>
            </div>
          ) : null}
        </div>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
