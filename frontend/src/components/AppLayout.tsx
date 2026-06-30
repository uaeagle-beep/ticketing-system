// Authenticated app shell: header with brand, nav (Board / Teams / Epics),
// and a collapsed user menu showing the email + Log out (Wireframe 1).

import { useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/AuthContext';

export function AppLayout() {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
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

  const initial = user?.email?.charAt(0).toUpperCase() ?? '?';

  return (
    <div className="app-shell">
      <header className="app-header">
        <NavLink to="/board" className="app-brand">
          Ticket Tracker
        </NavLink>
        <nav className="app-nav">
          <NavLink to="/board" className={({ isActive }) => (isActive ? 'active' : '')}>
            Board
          </NavLink>
          <NavLink to="/teams" className={({ isActive }) => (isActive ? 'active' : '')}>
            Teams
          </NavLink>
          <NavLink to="/epics" className={({ isActive }) => (isActive ? 'active' : '')}>
            Epics
          </NavLink>
          {/* Users zone is admin-only (ADR-0007). Hidden for members; route also guarded. */}
          {user?.isAdmin ? (
            <NavLink to="/users" className={({ isActive }) => (isActive ? 'active' : '')}>
              Users
            </NavLink>
          ) : null}
        </nav>
        <div className="app-header-spacer" />
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
            <span className="nowrap muted">{user?.email}</span>
          </button>
          {menuOpen ? (
            <div className="user-menu-popover" role="menu">
              <div className="user-menu-email">{user?.email}</div>
              <button
                type="button"
                className="btn btn-ghost"
                style={{ width: '100%', justifyContent: 'flex-start' }}
                onClick={handleLogout}
                role="menuitem"
              >
                Log out
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
