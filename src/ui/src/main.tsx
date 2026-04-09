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

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

boot()
