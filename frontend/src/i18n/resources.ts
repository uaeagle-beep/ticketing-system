// Aggregated i18next resource bundles (Wave 3 i18n, ADR-0022). Bundles are authored per
// feature-area namespace under src/locales/{uk,en}/*.json and imported statically so Vite
// bundles them (NO runtime CDN/HTTP fetch — respects the strict CSP `script-src 'self'`).
//
// `uk` is the source-of-truth language (the PO/users are Ukrainian, [ASSUMPTION W3-I18N-DEFAULT]);
// `en` is the parallel translation. The two are kept key-for-key in sync.

import enCommon from '@/locales/en/common.json';
import enAuth from '@/locales/en/auth.json';
import enBoard from '@/locales/en/board.json';
import enTickets from '@/locales/en/tickets.json';
import enComments from '@/locales/en/comments.json';
import enNotifications from '@/locales/en/notifications.json';
import enLabels from '@/locales/en/labels.json';
import enUsers from '@/locales/en/users.json';
import enAccount from '@/locales/en/account.json';
import enAnalytics from '@/locales/en/analytics.json';
import enIntegrations from '@/locales/en/integrations.json';
import enTeams from '@/locales/en/teams.json';
import enEpics from '@/locales/en/epics.json';
import enErrors from '@/locales/en/errors.json';
import enEnums from '@/locales/en/enums.json';
import enTime from '@/locales/en/time.json';

import ukCommon from '@/locales/uk/common.json';
import ukAuth from '@/locales/uk/auth.json';
import ukBoard from '@/locales/uk/board.json';
import ukTickets from '@/locales/uk/tickets.json';
import ukComments from '@/locales/uk/comments.json';
import ukNotifications from '@/locales/uk/notifications.json';
import ukLabels from '@/locales/uk/labels.json';
import ukUsers from '@/locales/uk/users.json';
import ukAccount from '@/locales/uk/account.json';
import ukAnalytics from '@/locales/uk/analytics.json';
import ukIntegrations from '@/locales/uk/integrations.json';
import ukTeams from '@/locales/uk/teams.json';
import ukEpics from '@/locales/uk/epics.json';
import ukErrors from '@/locales/uk/errors.json';
import ukEnums from '@/locales/uk/enums.json';
import ukTime from '@/locales/uk/time.json';

// The ordered namespace list (also the i18next `ns` array). `common` is the default namespace.
export const NAMESPACES = [
  'common',
  'auth',
  'board',
  'tickets',
  'comments',
  'notifications',
  'labels',
  'users',
  'account',
  'analytics',
  'integrations',
  'teams',
  'epics',
  'errors',
  'enums',
  'time',
] as const;

export const DEFAULT_NS = 'common';

export const resources = {
  en: {
    common: enCommon,
    auth: enAuth,
    board: enBoard,
    tickets: enTickets,
    comments: enComments,
    notifications: enNotifications,
    labels: enLabels,
    users: enUsers,
    account: enAccount,
    analytics: enAnalytics,
    integrations: enIntegrations,
    teams: enTeams,
    epics: enEpics,
    errors: enErrors,
    enums: enEnums,
    time: enTime,
  },
  uk: {
    common: ukCommon,
    auth: ukAuth,
    board: ukBoard,
    tickets: ukTickets,
    comments: ukComments,
    notifications: ukNotifications,
    labels: ukLabels,
    users: ukUsers,
    account: ukAccount,
    analytics: ukAnalytics,
    integrations: ukIntegrations,
    teams: ukTeams,
    epics: ukEpics,
    errors: ukErrors,
    enums: ukEnums,
    time: ukTime,
  },
} as const;

// The set of languages we ship (also gates the language switcher + locale validation).
export const SUPPORTED_LANGUAGES = ['uk', 'en'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const FALLBACK_LANGUAGE: SupportedLanguage = 'uk';

export function isSupportedLanguage(value: unknown): value is SupportedLanguage {
  return value === 'uk' || value === 'en';
}
