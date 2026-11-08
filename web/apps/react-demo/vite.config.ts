import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@demo-shared': fileURLToPath(new URL('../../demo-shared', import.meta.url)) } },
  server: { port: 5174, strictPort: true, proxy: { '/api': { target: process.env.APPROVAL_SMOKE_API ?? 'http://localhost:5199', changeOrigin: true } } },
  build: { outDir: 'dist', emptyOutDir: true },
  test: { environment: 'node', include: ['tests/**/*.test.{ts,tsx}'], setupFiles: ['tests/setup.ts'] },
})
