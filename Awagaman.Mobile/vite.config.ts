import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const proxyTarget = (env.VITE_DEV_PROXY_TARGET || 'http://127.0.0.1:5088').trim()
  const basePath = ((env.VITE_BASE_PATH || '/').trim() || '/')
  const normalizedBasePath = basePath.endsWith('/') ? basePath : `${basePath}/`

  return {
    base: normalizedBasePath,
    plugins: [
      react(),
      VitePWA({
        registerType: 'autoUpdate',
        includeAssets: ['favicon.svg', 'pwa-192.png', 'pwa-512.png'],
        manifest: {
          id: normalizedBasePath,
          name: 'Awagaman Mobile',
          short_name: 'Awagaman',
          description: 'Simple mobile ledger search for Awagaman ERP',
          theme_color: '#1d4ed8',
          background_color: '#eef2ff',
          display: 'fullscreen',
          display_override: ['fullscreen', 'standalone', 'minimal-ui', 'browser'],
          orientation: 'portrait',
          start_url: normalizedBasePath,
          scope: normalizedBasePath,
          icons: [
            { src: 'pwa-192.png', sizes: '192x192', type: 'image/png', purpose: 'any maskable' },
            { src: 'pwa-512.png', sizes: '512x512', type: 'image/png', purpose: 'any maskable' },
          ],
        },
        workbox: {
          globPatterns: ['**/*.{js,css,html,svg,png}'],
          runtimeCaching: [
            {
              urlPattern: ({ url }) => url.pathname.startsWith('/api/'),
              handler: 'NetworkOnly',
            },
          ],
        },
      }),
    ],
    server: {
      host: '0.0.0.0',
      port: 5174,
      proxy: {
        '/api': {
          target: proxyTarget,
          changeOrigin: true,
        },
      },
    },
  }
})
