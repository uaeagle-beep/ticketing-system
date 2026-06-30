/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// The SPA is served from the domain root by nginx (ADR-0005), so base is '/'.
// All API calls are made to the relative path '/api' (single-origin via the
// nginx reverse proxy in production). For local `vite dev`, we proxy '/api' to
// a backend so the same relative URLs work without rebuild.
export default defineConfig({
  base: '/',
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        // Override with VITE_DEV_API_TARGET when running the API on another host/port.
        target: process.env.VITE_DEV_API_TARGET ?? 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
  // Vitest configuration (frontend unit/component tests, no backend required).
  // Runs in jsdom with React Testing Library; network is mocked via MSW
  // (see src/test/setup.ts). `globals: true` exposes describe/it/expect/vi
  // without per-file imports, matching the jest-dom matcher augmentation.
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // Vitest runs ONLY the jsdom unit/component suite under src/. The Playwright
    // E2E specs live in ./e2e and use @playwright/test (a different runner), so
    // they must be excluded from Vitest's default {test,spec} glob — otherwise
    // `npm run test` would try to load them and fail on the Playwright import.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],
    // Restore spies/mocks and reset module/timer state between tests so suites
    // stay isolated even when running in the same worker.
    clearMocks: true,
    restoreMocks: true,
    css: false,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      // Test scaffolding and pure type modules carry no runtime behaviour.
      exclude: [
        'src/test/**',
        'src/**/*.test.{ts,tsx}',
        'src/main.tsx',
        'src/vite-env.d.ts',
        'src/api/types.ts',
        '**/*.config.*',
      ],
    },
  },
});
