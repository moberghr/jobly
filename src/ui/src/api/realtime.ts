import { config } from '@/config';

export type RealtimeEvent = 'JobFinalized' | 'MessageEnqueued';

export function getHubUrl(): string {
  // apiPath is e.g. "/warp/api/". Hub lives at "/warp/api/hub".
  const base = config.apiPath.replace(/\/+$/, '');
  return `${base}/hub`;
}
