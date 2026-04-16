import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'
import fs from 'fs'

/**
 * In demo mode, serves UI extension JS files from their source location
 * so Playwright screenshot tests can load them via dynamic import().
 */
function serveExtensions(): Plugin {
  const extDir = path.resolve(__dirname, '../core/Jobly.UI/Extensions')
  return {
    name: 'serve-extensions',
    // Use enforce: 'pre' so this runs before Vite's transform pipeline
    enforce: 'pre',
    configureServer(server) {
      // Pre-hook: runs before Vite's SPA fallback so extension JS is served directly
      server.middlewares.use((req, res, next) => {
          // Strip Vite's ?import query parameter
          const rawUrl = req.url ?? ''
          const url = rawUrl.split('?')[0]
          const prefix = '/jobly/_ext/'
          if (!url.startsWith(prefix)) {
            return next()
          }

          // Map /_ext/{name}/file.js → Extensions/{Name}/dist/file.js
          const relPath = url.slice(prefix.length)
          const [extName, ...rest] = relPath.split('/')
          const filePath = path.join(extDir, extName.charAt(0).toUpperCase() + extName.slice(1), 'dist', ...rest)

          if (fs.existsSync(filePath)) {
            res.setHeader('Content-Type', 'application/javascript')
            fs.createReadStream(filePath).pipe(res)
          } else {
            next()
          }
        })
    },
  }
}

export default defineConfig(({ mode }) => {
  const isDemo = mode === 'demo'

  return {
    plugins: [react(), tailwindcss(), serveExtensions()],
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
