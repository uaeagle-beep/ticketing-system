// Display-name resolution (USER_MANAGEMENT_DESIGN Feature 1). The display value for a person is
// their optional name when set; otherwise their email. Email stays the login/account key — this is
// purely how a user is *shown* (Users list, header, ticket "Created by", comment author). Used
// everywhere a person is rendered so the fallback is consistent.

/**
 * Resolve how to display a person. Returns the trimmed `name` when it is a non-empty string,
 * otherwise `email`. A whitespace-only name falls back to the email (mirrors the backend, which
 * stores blank names as null).
 */
export function displayName(name: string | null | undefined, email: string): string {
  const trimmed = name?.trim();
  return trimmed ? trimmed : email;
}
