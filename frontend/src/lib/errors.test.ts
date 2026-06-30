import { describe, expect, it } from 'vitest';
import { errorMessage, isApiErrorCode } from './errors';
import { ApiError } from '@/api/client';

describe('errorMessage', () => {
  it('prefers per-field validation text when present', () => {
    const err = new ApiError(400, 'validation_error', 'Validation failed.', {
      email: ['Email is required.'],
      password: ['Must be at least 8 characters.'],
    });
    // fieldErrorText flattens the per-field map (client.ts), space-joined.
    expect(errorMessage(err)).toBe('Email is required. Must be at least 8 characters.');
  });

  it('uses a friendly message for known codes (duplicate_team_name)', () => {
    const err = new ApiError(409, 'duplicate_team_name', 'server msg');
    expect(errorMessage(err)).toBe('A team with this name already exists.');
  });

  it('maps account_not_verified to the friendly verification hint', () => {
    const err = new ApiError(403, 'account_not_verified', 'server msg');
    expect(errorMessage(err)).toBe(
      'Your account is not verified. Check your email or request a new verification link.',
    );
  });

  it('maps invalid_credentials to the generic anti-enumeration message', () => {
    const err = new ApiError(401, 'invalid_credentials', 'server msg');
    expect(errorMessage(err)).toBe('Invalid email or password.');
  });

  it('falls back to the server message for an unknown code', () => {
    const err = new ApiError(418, 'some_new_code', 'A very specific server message.');
    expect(errorMessage(err)).toBe('A very specific server message.');
  });

  it('falls back to a generic validation message when no field errors and no friendly entry', () => {
    // validation_error with no field map and no FRIENDLY entry -> server message.
    const err = new ApiError(400, 'validation_error', 'Bad request body.');
    expect(errorMessage(err)).toBe('Bad request body.');
  });

  it('handles a plain Error by returning its message', () => {
    expect(errorMessage(new Error('network down'))).toBe('network down');
  });

  it('handles a non-Error value with a generic message', () => {
    expect(errorMessage('weird string')).toBe('Something went wrong.');
    expect(errorMessage(null)).toBe('Something went wrong.');
  });
});

describe('isApiErrorCode', () => {
  it('is true only for an ApiError with the matching code', () => {
    const err = new ApiError(403, 'account_not_verified', 'msg');
    expect(isApiErrorCode(err, 'account_not_verified')).toBe(true);
    expect(isApiErrorCode(err, 'invalid_credentials')).toBe(false);
  });

  it('is false for non-ApiError values', () => {
    expect(isApiErrorCode(new Error('x'), 'account_not_verified')).toBe(false);
    expect(isApiErrorCode(null, 'account_not_verified')).toBe(false);
  });
});
