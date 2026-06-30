import { describe, expect, it, vi } from 'vitest';
import { getToken, setToken, clearToken, subscribeToken } from './tokenStore';

// The store keeps an in-memory token mirrored into localStorage under
// 'tt.auth.token' (ADR-0001). The global test setup clears both after each
// test, so each case starts from a clean, logged-out state.
const STORAGE_KEY = 'tt.auth.token';

describe('tokenStore', () => {
  it('starts with no token', () => {
    expect(getToken()).toBeNull();
  });

  it('setToken stores the token in memory and mirrors it to localStorage', () => {
    setToken('abc123');
    expect(getToken()).toBe('abc123');
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('abc123');
  });

  it('setToken(null) removes the localStorage mirror', () => {
    setToken('abc123');
    setToken(null);
    expect(getToken()).toBeNull();
    expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('clearToken wipes the token (equivalent to setToken(null))', () => {
    setToken('to-be-cleared');
    clearToken();
    expect(getToken()).toBeNull();
    expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('notifies subscribers of token changes and supports unsubscribe', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeToken(listener);

    setToken('new-token');
    expect(listener).toHaveBeenCalledWith('new-token');

    clearToken();
    expect(listener).toHaveBeenCalledWith(null);

    listener.mockClear();
    unsubscribe();
    setToken('after-unsubscribe');
    expect(listener).not.toHaveBeenCalled();
  });
});
