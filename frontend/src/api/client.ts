// Low-level HTTP client for the Ticket Tracker API.
//
// - Base path is the relative '/api' (single-origin via nginx proxy, ADR-0005).
// - Attaches `Authorization: Bearer <token>` when a token is present.
// - Parses the uniform error envelope ({ error: { code, message, errors? } }).
// - On 401 it clears the token (so the auth gate redirects to /login), EXCEPT
//   for the auth endpoints themselves where a 401 is an expected domain result
//   (e.g. invalid_credentials on login) that must surface to the caller.

import { clearToken, getToken } from './tokenStore';
import type { ApiErrorBody, ApiErrorCode } from './types';

export const API_BASE = '/api';

export class ApiError extends Error {
  readonly status: number;
  readonly code: ApiErrorCode;
  readonly fieldErrors?: Record<string, string[]>;

  constructor(
    status: number,
    code: ApiErrorCode,
    message: string,
    fieldErrors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }

  /** Flatten the per-field error map into a single readable string, if present. */
  fieldErrorText(): string | null {
    if (!this.fieldErrors) return null;
    const parts: string[] = [];
    for (const messages of Object.values(this.fieldErrors)) {
      for (const m of messages) parts.push(m);
    }
    return parts.length ? parts.join(' ') : null;
  }
}

// Endpoints where a 401 is a legitimate response that must NOT trigger a global
// logout/redirect (the user is not logged in yet, or is checking credentials).
const AUTH_PUBLIC_PATHS = new Set<string>([
  '/auth/login',
  '/auth/signup',
  '/auth/verify-email',
  '/auth/resend-verification',
]);

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  /** Query params; undefined/empty values are omitted. */
  query?: Record<string, string | number | boolean | undefined | null>;
  signal?: AbortSignal;
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = `${API_BASE}${path}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    params.append(key, String(value));
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

async function parseError(res: Response): Promise<ApiError> {
  let code: ApiErrorCode = 'unknown_error';
  let message = `Request failed (${res.status}).`;
  let fieldErrors: Record<string, string[]> | undefined;

  try {
    const data = (await res.json()) as Partial<ApiErrorBody>;
    if (data && typeof data === 'object' && data.error) {
      code = data.error.code ?? code;
      message = data.error.message ?? message;
      fieldErrors = data.error.errors;
    }
  } catch {
    // Non-JSON error body (e.g. nginx 502); keep the generic message.
    if (res.status === 502 || res.status === 503) {
      code = 'service_unavailable';
      message = 'The server is unavailable. Please try again in a moment.';
    }
  }
  return new ApiError(res.status, code, message, fieldErrors);
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, query, signal } = options;

  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers['Authorization'] = `Bearer ${token}`;

  let payload: BodyInit | undefined;
  if (body !== undefined) {
    if (body instanceof FormData) {
      // Multipart upload: let the browser set Content-Type (with the multipart boundary).
      payload = body;
    } else {
      headers['Content-Type'] = 'application/json; charset=utf-8';
      payload = JSON.stringify(body);
    }
  }

  const res = await fetch(buildUrl(path, query), {
    method,
    headers,
    body: payload,
    signal,
  });

  if (res.status === 401 && !AUTH_PUBLIC_PATHS.has(path)) {
    // Token rejected on a protected endpoint -> drop it so the guard sends the
    // user to /login. We still throw so the caller's error path runs.
    clearToken();
  }

  if (!res.ok) {
    throw await parseError(res);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  // Some endpoints (e.g. 201/200 with bodies) return JSON; tolerate empty bodies.
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
}

/**
 * Fetch a binary resource (e.g. an attachment download) with the bearer token attached, returning the
 * response Blob. Attachment downloads are authenticated + forced-download (Content-Disposition:
 * attachment), so they cannot be a plain `<a href>` — the SPA fetches the blob and triggers a download
 * (Wave 3, ADR-0018 / §10.1). Errors are parsed into the same ApiError envelope as the JSON path.
 */
export async function requestBlob(path: string, signal?: AbortSignal): Promise<Blob> {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const res = await fetch(buildUrl(path), { method: 'GET', headers, signal });

  if (res.status === 401 && !AUTH_PUBLIC_PATHS.has(path)) {
    clearToken();
  }
  if (!res.ok) {
    throw await parseError(res);
  }
  return res.blob();
}

export const http = {
  get: <T>(path: string, query?: RequestOptions['query'], signal?: AbortSignal) =>
    request<T>(path, { method: 'GET', query, signal }),
  post: <T>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body }),
  put: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PATCH', body }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
  getBlob: (path: string, signal?: AbortSignal) => requestBlob(path, signal),
};
