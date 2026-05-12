import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

async function boot() {
  const isDemoMode = new URLSearchParams(window.location.search).has('demo')
    || import.meta.env.VITE_DEMO === 'true'

  if (isDemoMode) {
    const { setupDemo } = await import('@/demo')
    setupDemo()
  }

  // Note: realtime probe + hub connection is NOT started here. It runs from
  // MainLayout's useEffect so that page-level useRealtimeRefetch subscribers
  // are guaranteed to have registered before the post-connect drain emits.
  // Triggering it at module load races React's useEffect cycle and the drain
  // fires before subscribers exist, leaving the dashboard stale until the 30s
  // safety-net interval.

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

boot()
