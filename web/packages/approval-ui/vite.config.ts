import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 库模式：输出 ES 模块与样式；Vue、Element Plus、Vue Flow 等由宿主提供
const external = [/^vue($|\/)/, /^vue-router/, /^element-plus/, /^@element-plus\//, /^@vue-flow\//, /^@dagrejs\//]

export default defineConfig({
  plugins: [vue()],
  build: {
    lib: {
      entry: fileURLToPath(new URL('./src/index.ts', import.meta.url)),
      formats: ['es'],
      fileName: 'index',
      cssFileName: 'approval-ui',
    },
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: { external },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
})
