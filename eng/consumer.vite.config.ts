import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

const proxy = { '/api': { target: process.env.APPROVAL_SMOKE_API ?? 'http://127.0.0.1:1', changeOrigin: true } }
export default defineConfig({
  plugins: [vue()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)), '@demo-shared': fileURLToPath(new URL('./demo-shared', import.meta.url)) } },
  server: { host: '127.0.0.1', strictPort: true, proxy },
  preview: { host: '127.0.0.1', strictPort: true, proxy },
  build: { outDir: 'dist', emptyOutDir: true },
})
