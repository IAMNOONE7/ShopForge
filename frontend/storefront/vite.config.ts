import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // The API identifies the store by hostname, so the browser's Host header must reach it unchanged.
      '/api': { target: process.env.SHOPFORGE_API_TARGET ?? 'http://localhost:5080', changeOrigin: false },
    },
  },
})
