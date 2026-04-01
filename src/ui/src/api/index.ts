import api from './client';
import type { DashboardStatistics, JobModel, JobDetailModel, JobGroupModel, JobGroupDetailModel, RecurringJobModel, RecurringJobDetailModel, ServerModel, ServerTaskSummary, ServerLogModel, PagedList, BulkResult, StatsHistoryPoint, TypeCountModel, WorkerDetailModel, WorkerJobLogModel } from '@/types';

// Dashboard
export const getStatus = () => api.get<DashboardStatistics>('/status').then(r => r.data);

// Jobs by state
export const getEnqueuedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/enqueued', { params: { page, pageSize } }).then(r => r.data);

export const getCompletedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/completed', { params: { page, pageSize } }).then(r => r.data);

export const getFailedJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/failed', { params: { page, pageSize } }).then(r => r.data);

export const getFailedJobTypes = () =>
  api.get<TypeCountModel[]>('/jobs/failed/types').then(r => r.data);

export const getFailedJobsByType = (type: string, page = 0, pageSize = 20) =>
  api.get<PagedList<JobModel>>('/jobs/failed/by-type', { params: { type, page, pageSize } }).then(r => r.data);

export const deleteFailedJobsByType = (type: string) =>
  api.post<BulkResult>('/jobs/failed/delete-by-type', null, { params: { type } }).then(r => r.data);

export const requeueFailedJobsByType = (type: string) =>
  api.post<BulkResult>('/jobs/failed/requeue-by-type', null, { params: { type } }).then(r => r.data);

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
  api.get<PagedList<JobGroupModel>>('/messages', { params: { page, pageSize, state } }).then(r => r.data);

export const getMessageById = (messageId: string) =>
  api.get<JobGroupDetailModel>(`/messages/${messageId}`).then(r => r.data);

export const getMessageJobCounts = (messageId: string) =>
  api.get<Record<string, number>>(`/messages/${messageId}/jobs/counts`).then(r => r.data);

export const getMessageJobs = (messageId: string, page = 0, pageSize = 20, state?: string) =>
  api.get<PagedList<JobModel>>(`/messages/${messageId}/jobs`, { params: { page, pageSize, state } }).then(r => r.data);

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
  api.get<PagedList<JobGroupModel>>('/batches', { params: { page, pageSize, state } }).then(r => r.data);

export const getBatchById = (batchId: string) =>
  api.get<JobGroupDetailModel>(`/batches/${batchId}`).then(r => r.data);

export const getBatchJobCounts = (batchId: string) =>
  api.get<Record<string, number>>(`/batches/${batchId}/jobs/counts`).then(r => r.data);

export const getBatchJobs = (batchId: string, page = 0, pageSize = 20, state?: string) =>
  api.get<PagedList<JobModel>>(`/batches/${batchId}/jobs`, { params: { page, pageSize, state } }).then(r => r.data);

// Servers
export const getServers = () => api.get<ServerModel[]>('/servers').then(r => r.data);

export const getServerById = (serverId: string) =>
  api.get<ServerModel>(`/servers/${serverId}`).then(r => r.data);

export const getServerTaskSummaries = (serverId: string) =>
  api.get<ServerTaskSummary[]>(`/servers/${serverId}/tasks`).then(r => r.data);

export const getServerLogs = (serverId: string, page = 0, pageSize = 20, taskName?: string) =>
  api.get<PagedList<ServerLogModel>>(`/servers/${serverId}/logs`, { params: { page, pageSize, taskName } }).then(r => r.data);

export const getWorkerById = (workerId: string) =>
  api.get<WorkerDetailModel>(`/workers/${workerId}`).then(r => r.data);

export const getWorkerJobLogs = (workerId: string, page = 0, pageSize = 20) =>
  api.get<PagedList<WorkerJobLogModel>>(`/workers/${workerId}/logs`, { params: { page, pageSize } }).then(r => r.data);

export const getStatsHistory = (hours = 24) =>
  api.get<StatsHistoryPoint[]>('/stats/history', { params: { hours } }).then(r => r.data);
