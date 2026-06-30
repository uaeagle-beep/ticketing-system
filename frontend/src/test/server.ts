// Shared MSW server for Node/jsdom tests. Started/stopped in src/test/setup.ts.
import { setupServer } from 'msw/node';
import { handlers } from './handlers';

export const server = setupServer(...handlers);
