import api from './client';
import type { DashboardStatistics, JobModel, JobDetailModel, MessageModel, MessageDetailModel, RecurringJobModel, ServerModel, PagedList } from '@/types';

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

// Job details & actions
export const getJobById = (jobId: string) =>
  api.get<JobDetailModel>(`/jobs/${jobId}`).then(r => r.data);

export const requeueJob = (jobId: string) => api.post(`/jobs/${jobId}/requeue`);
export const deleteJob = (jobId: string) => api.post(`/jobs/${jobId}/delete`);

// Messages
export const getMessages = (page = 0, pageSize = 20) =>
  api.get<PagedList<MessageModel>>('/messages', { params: { page, pageSize } }).then(r => r.data);

export const getMessageById = (messageId: string) =>
  api.get<MessageDetailModel>(`/messages/${messageId}`).then(r => r.data);

// Recurring jobs
export const getRecurringJobs = (page = 0, pageSize = 20) =>
  api.get<PagedList<RecurringJobModel>>('/recurring', { params: { page, pageSize } }).then(r => r.data);

export const triggerRecurringJob = (id: number) => api.post(`/recurring/${id}/trigger`);
export const deleteRecurringJob = (id: number) => api.delete(`/recurring/${id}`);

// Servers
export const getServers = () => api.get<ServerModel[]>('/servers').then(r => r.data);
