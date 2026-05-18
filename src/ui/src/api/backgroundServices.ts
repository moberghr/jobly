import axios from 'axios';
import api from './client';
import type {
  BackgroundServiceListItem,
  BackgroundServiceDetail,
  BackgroundServiceLeaseDto,
  BackgroundServiceLogDto,
  BackgroundServiceLogSource,
  LogLevel,
} from '@/types/backgroundServices';

export const getBackgroundServices = (): Promise<BackgroundServiceListItem[]> =>
  api.get<BackgroundServiceListItem[]>('/services').then(r => r.data);

export const getBackgroundService = (name: string): Promise<BackgroundServiceDetail | null> =>
  api
    .get<BackgroundServiceDetail>(`/services/${encodeURIComponent(name)}`)
    .then(r => r.data)
    .catch(e => {
      if (axios.isAxiosError(e) && e.response?.status === 404) {
        return null;
      }
      throw e;
    });

export const getBackgroundServiceLease = (name: string): Promise<BackgroundServiceLeaseDto | null> =>
  api
    .get<BackgroundServiceLeaseDto>(`/services/${encodeURIComponent(name)}/lease`)
    .then(r => r.data)
    .catch(e => {
      if (axios.isAxiosError(e) && e.response?.status === 404) {
        return null;
      }
      throw e;
    });

export interface GetBackgroundServiceLogsOptions {
  source?: BackgroundServiceLogSource;
  minLevel?: LogLevel;
  fromId?: number;
  limit?: number;
}

export const getBackgroundServiceLogs = (
  name: string,
  opts: GetBackgroundServiceLogsOptions = {},
): Promise<BackgroundServiceLogDto[]> => {
  const params: Record<string, string | number> = {};
  if (opts.source !== undefined) {
    params.source = opts.source;
  }
  if (opts.minLevel !== undefined) {
    params.level = opts.minLevel;
  }
  if (opts.fromId !== undefined) {
    params.fromId = opts.fromId;
  }
  if (opts.limit !== undefined) {
    params.limit = opts.limit;
  }

  return api
    .get<BackgroundServiceLogDto[]>(`/services/${encodeURIComponent(name)}/logs`, { params })
    .then(r => r.data);
};
