import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

export default defineConfig(({ mode }) => {
  const isDemo = mode === 'demo'

  return {
    plugins: [react(), tailwindcss()],
    base: './',
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    build: {
      outDir: '../core/Jobly.UI/dist',
      emptyOutDir: true,
    },
    server: isDemo
      ? {}
      : {
          proxy: {
            '/jobly': 'http://localhost:5104',
          },
        },
  }
})
