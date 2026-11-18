/// <reference types="vitest/config" />
import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Under Aspire the API address is injected as services__api__http__0; standalone it defaults to :5080.
const apiTarget = process.env.services__api__http__0 ?? process.env.API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          react: ['react', 'react-dom', 'react-router'],
          data: ['@tanstack/react-query', 'zustand', 'zod', 'react-hook-form'],
          markdown: ['react-markdown', 'remark-gfm'],
          signalr: ['@microsoft/signalr'],
        },
      },
    },
  },
  server: {
    port: Number(process.env.PORT) || 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/hubs': { target: apiTarget, changeOrigin: true, ws: true },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
