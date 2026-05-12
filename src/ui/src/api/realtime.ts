import api from './client';
import { config } from '@/config';

export type RealtimeEvent = 'JobFinalized' | 'MessageEnqueued';

export async function probeDashboardPush(): Promise<boolean> {
  try {
    const response = await api.get<{ enabled: boolean }>('/dashboard/push/probe', { validateStatus: () => true });
    return response.status === 200 && response.data?.enabled === true;
  } catch {
    return false;
  }
}

export function getHubUrl(): string {
  // apiPath is e.g. "/warp/api/". Hub lives at "/warp/api/hub".
  const base = config.apiPath.replace(/\/+$/, '');
  return `${base}/hub`;
}
