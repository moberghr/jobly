import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 30000,
  expect: { timeout: 10000 },
  fullyParallel: true,
  workers: process.env.CI ? 2 : undefined,
  use: {
    baseURL: 'http://localhost:5179',
    viewport: { width: 1280, height: 800 },
    actionTimeout: 10000,
  },
  webServer: {
    command: 'npx vite --mode demo --port 5179',
    url: 'http://localhost:5179',
    reuseExistingServer: !process.env.CI,
    timeout: 30000,
  },
});
