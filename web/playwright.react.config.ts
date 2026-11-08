import { defineConfig } from '@playwright/test'
export default defineConfig({
  testDir: './tests/browser/react', workers: 1, retries: 0, timeout: 60000,
  outputDir: `${process.env.APPROVAL_SMOKE_OUTPUT}/test-results`,
  reporter: [['list'], ['json', { outputFile: `${process.env.APPROVAL_SMOKE_OUTPUT}/results.json` }]],
  use: { baseURL: process.env.APPROVAL_REACT_URL, viewport: { width: 1600, height: 1100 }, headless: true, channel: process.env.APPROVAL_BROWSER_CHANNEL ?? 'msedge', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
})
