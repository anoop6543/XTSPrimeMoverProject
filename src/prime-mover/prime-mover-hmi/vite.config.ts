import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      // All /api/* calls go to PrimeMoverApi (aggregating layer on port 8082)
      '/api': { target: 'http://localhost:8082', changeOrigin: true },
      // SignalR WebSocket hub lives on PrimeMoverApi as well
      '/hubs': { target: 'http://localhost:8082', ws: true, changeOrigin: true }
    }
  }
})
