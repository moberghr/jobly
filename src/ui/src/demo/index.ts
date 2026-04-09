import api from '@/api/client';
import { createDemoAdapter } from './adapter';
import { useDashboardStore } from '@/stores/dashboard';
import { generateRealtimeHistory } from './data';

export function setupDemo() {
  const isLoginMode = new URLSearchParams(window.location.search).has('login');

  // Replace network transport — all API calls return mock data
  api.defaults.adapter = createDemoAdapter(isLoginMode);

  // Pre-seed the realtime chart with 60 seconds of data so it's immediately full
  useDashboardStore.setState({ realtimeData: generateRealtimeHistory() });

  // Support ?login param for login page screenshots
  if (isLoginMode) {
    window.hasBuiltInLogin = true;
  }
}
