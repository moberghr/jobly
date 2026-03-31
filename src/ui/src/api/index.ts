import api from './client';
import type { DashboardStatistics, JobModel, JobDetailModel, MessageModel, MessageDetailModel, RecurringJobModel, RecurringJobDetailModel, ServerModel, ServerTaskSummary, ServerLogModel, PagedList, BulkResult, StatsHistoryPoint, BatchModel, BatchDetailModel } from '@/types';

// Dashboard
export const getStatus = () => api.get<DashboardStatistics>('/status').then(r => r.data);

// Jobs by state
export const getEnqueuedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/enqueued', { params: { page, pageSize } }).then(r => r.data);

export const getCompletedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/completed', { params: { page, pageSize } }).then(r => r.data);

export const getFailedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/failed', { params: { page, pageSize } }).then(r => r.data);

export const getProcessingJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/processing', { params: { page, pageSize } }).then(r => r.data);

export const getScheduledJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/scheduled', { params: { page, pageSize } }).then(r => r.data);

export const getAwaitingJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/awaiting', { params: { page, pageSize } }).then(r => r.data);

export const getDeletedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/deleted', { params: { page, pageSize } }).then(r => r.data);

// Job details & actions
export const getJobById = (jobId: string) =>
  api.get<JobDetailModel>(`/jobs/${jobId}`).then(r => r.data);

export const getSiblingJobs = (jobId: string, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>(`/jobs/${jobId}/siblings`, { params: { page, pageSize } }).then(r => r.data);

export const getChildJobs = (jobId: string, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>(`/jobs/${jobId}/children`, { params: { page, pageSize } }).then(r => r.data);

export const getTraceJobs = (jobId: string, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>(`/jobs/${jobId}/trace`, { params: { page, pageSize } }).then(r => r.data);

export const requeueJob = (jobId: string) => api.post(`/jobs/${jobId}/requeue`);
export const deleteJob = (jobId: string) => api.post(`/jobs/${jobId}/delete`);

// Messages
export const getMessages = (page = 0, pageSize = 20, state?: string) =>
  api.get<PagedList<MessageModel>>('/messages', { params: { page, pageSize, state } }).then(r => r.data);

export const getMessageById = (messageId: string) =>
  api.get<MessageDetailModel>(`/messages/${messageId}`).then(r => r.data);

export const getMessageJobs = (messageId: string, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>(`/messages/${messageId}/jobs`, { params: { page, pageSize } }).then(r => r.data);

// Recurring jobs
export const getRecurringJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<RecurringJobModel>>('/recurring', { params: { page, pageSize } }).then(r => r.data);

export const getRecurringJobById = (id: number) =>
  api.get<RecurringJobDetailModel>(`/recurring/${id}`).then(r => r.data);

export const getRecurringJobJobs = (id: number, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>(`/recurring/${id}/jobs`, { params: { page, pageSize } }).then(r => r.data);

export const triggerRecurringJob = (id: number) => api.post(`/recurring/${id}/trigger`);
export const deleteRecurringJob = (id: number) => api.delete(`/recurring/${id}`);

// Bulk actions
export const bulkDeleteJobs = (jobIds: string[]) =>
  api.post<BulkResult>('/jobs/bulk/delete', { jobIds }).then(r => r.data);

export const bulkRequeueJobs = (jobIds: string[]) =>
  api.post<BulkResult>('/jobs/bulk/requeue', { jobIds }).then(r => r.data);

// Batches
export const getBatches = (page = 0, pageSize = 20, state?: string) =>
  api.get<PagedList<BatchModel>>('/batches', { params: { page, pageSize, state } }).then(r => r.data);

export const getBatchById = (batchId: string) =>
  api.get<BatchDetailModel>(`/batches/${batchId}`).then(r => r.data);

export const getBatchJobs = (batchId: string, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>(`/batches/${batchId}/jobs`, { params: { page, pageSize } }).then(r => r.data);

// Servers
export const getServers = () => api.get<ServerModel[]>('/servers').then(r => r.data);

export const getServerById = (serverId: string) =>
  api.get<ServerModel>(`/servers/${serverId}`).then(r => r.data);

export const getServerTaskSummaries = (serverId: string) =>
  api.get<ServerTaskSummary[]>(`/servers/${serverId}/tasks`).then(r => r.data);

export const getServerLogs = (serverId: string, page = 0, pageSize = 20, taskName?: string) =>
  api.get<PagedList<ServerLogModel>>(`/servers/${serverId}/logs`, { params: { page, pageSize, taskName } }).then(r => r.data);

export const getStatsHistory = (hours = 24) =>
  api.get<StatsHistoryPoint[]>('/stats/history', { params: { hours } }).then(r => r.data);
