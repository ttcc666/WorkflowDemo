import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 开发：Vite 开发服务器把 /api 代理到 Certificate.Api（dotnet run --urls http://localhost:5199）
// 构建：产物输出到示例后端的 wwwroot，由 ASP.NET Core 直接托管
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5199', changeOrigin: true },
    },
  },
  build: {
    outDir: '../../samples/Certificate.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500,
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
