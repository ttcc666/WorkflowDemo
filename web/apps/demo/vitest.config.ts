import { defineConfig } from 'vitest/config'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  resolve: { alias: {
    '@': fileURLToPath(new URL('./src', import.meta.url)),
    '@ui': fileURLToPath(new URL('../../packages/approval-ui/src', import.meta.url)),
  } },
  test: { environment: 'node', include: ['tests/**/*.test.ts'] },
})
