import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: 'https://localhost:7149', changeOrigin: true, secure: false },
      '/health': { target: 'https://localhost:7149', changeOrigin: true, secure: false },
      '/openapi': { target: 'https://localhost:7149', changeOrigin: true, secure: false },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
  },
})
