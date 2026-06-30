import { Link } from 'react-router-dom';

export function NotFoundPage() {
  return (
    <div className="auth-shell">
      <div className="auth-card" style={{ textAlign: 'center' }}>
        <h1 style={{ fontSize: 22, marginBottom: 8 }}>Page not found</h1>
        <p className="muted" style={{ marginBottom: 16 }}>
          The page you’re looking for doesn’t exist.
        </p>
        <Link to="/board" className="btn btn-primary">
          Go to board
        </Link>
      </div>
    </div>
  );
}
