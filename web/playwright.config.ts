import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './tests/browser',
  testIgnore: '**/react/**',
  workers: 1,
  retries: 0,
  timeout: 60000,
  outputDir: `${process.env.APPROVAL_SMOKE_OUTPUT ?? '../output/playwright'}/test-results`,
  reporter: [['list'], ['json', { outputFile: `${process.env.APPROVAL_SMOKE_OUTPUT ?? '../output/playwright'}/results.json` }]],
  use: {
    baseURL: process.env.APPROVAL_SMOKE_URL,
    headless: true,
    channel: process.env.APPROVAL_BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined),
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
})
