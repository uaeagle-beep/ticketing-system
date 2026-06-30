// Auth-token store: the token lives in memory (authoritative for the running
// app) and is mirrored into localStorage so a page refresh keeps the session
// (ADR-0001). Per source §9, localStorage is NOT the system of record for
// application data — it only holds the auth token, exactly as a cookie would.
// The token is never placed in any URL.

const STORAGE_KEY = 'tt.auth.token';

let inMemoryToken: string | null = null;

// Subscribers are notified when the token changes (e.g. on a 401-triggered
// logout) so the auth context can react without prop drilling.
type Listener = (token: string | null) => void;
const listeners = new Set<Listener>();

function readStorage(): string | null {
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeStorage(token: string | null): void {
  try {
    if (token === null) {
      window.localStorage.removeItem(STORAGE_KEY);
    } else {
      window.localStorage.setItem(STORAGE_KEY, token);
    }
  } catch {
    // Ignore storage failures (private mode, quota); in-memory token still works.
  }
}

// Initialize from the localStorage mirror at module load.
inMemoryToken = readStorage();

export function getToken(): string | null {
  return inMemoryToken;
}

export function setToken(token: string | null): void {
  inMemoryToken = token;
  writeStorage(token);
  for (const listener of listeners) listener(token);
}

export function clearToken(): void {
  setToken(null);
}

export function subscribeToken(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
