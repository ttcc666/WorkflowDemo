import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { readFileSync } from 'node:fs'
const dependencies = Object.keys(JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')).dependencies)
const pages = ['WorkbenchPage', 'ApprovalDetailPage', 'StartCenterPage', 'StartFormPage', 'DefinitionsPage', 'DesignerPage', 'MonitorPage', 'OutboxPage', 'DiagnosticsPage']
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    lib: { entry: { index: 'src/library.ts', ...Object.fromEntries(pages.map(page => ['pages/' + page, 'src/pages/' + page + '.tsx'])) }, formats: ['es'], fileName: (_format, name) => name + '.js', cssFileName: 'approval-react-ui' },
    rollupOptions: { external: (id: string) => !id.endsWith('.css') && (/^react(?:-dom|-router)?($|\/)/.test(id) || dependencies.some(name => id === name || id.startsWith(name + '/'))) },
  },
  test: { environment: 'jsdom', include: ['tests/**/*.test.{ts,tsx}'], setupFiles: ['tests/setup.ts'] },
})
