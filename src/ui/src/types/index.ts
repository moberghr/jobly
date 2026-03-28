export const State = {
  Enqueued: 1,
  Awaiting: 2,
  Processing: 3,
  Completed: 4,
  Failed: 5,
  Deleted: 6,
} as const;
export type State = (typeof State)[keyof typeof State];

export interface DashboardStatistics {
  total: number;
  pending: number;
  scheduled: number;
  created: number;
  completed: number;
  failed: number;
  processing: number;
  servers: number;
  awaiting: number;
  messages: number;
  totalSucceeded: number;
  totalFailed: number;
  totalDeleted: number;
  totalCreated: number;
}

export interface JobModel {
  id: string;
  type: string;
  message: string;
  createTime: string;
  scheduleTime: string | null;
  processedTime: string | null;
  currentState: State;
}

export interface JobDetailModel extends JobModel {
  handlerType: string | null;
  messageId: string | null;
  parentJobId: string | null;
  batchId: string | null;
  retriedTimes: number;
  maxRetries: number;
  stateHistory: JobStateModel[];
  logs: JobLogModel[];
  siblingJobs: JobModel[];
  childJobs: JobModel[];
}

export interface JobLogModel {
  id: string;
  timestamp: string;
  level: string;
  message: string;
  exception: string | null;
}

export interface JobStateModel {
  id: string;
  state: State;
  dateTime: string;
  message: string | null;
  jobId: string;
}

export interface MessageModel {
  id: string;
  type: string;
  payload: string;
  queue: string;
  currentState: State;
  jobCount: number;
  createTime: string;
}

export interface MessageDetailModel extends MessageModel {
  jobs: JobModel[];
}

export interface RecurringJobModel {
  id: number;
  name: string;
  cron: string;
  type: string;
  nextExecution: string | null;
  lastExecution: string | null;
  createdAt: string;
}

export interface ServerModel {
  id: string;
  serverName: string;
  startedTime: string;
  lastHeartbeatTime: string;
  serviceCount: number;
  workers: WorkerModel[];
}

export interface WorkerModel {
  workerId: string;
  startedTime: string;
  lastHeartbeatTime: string | null;
  currentJobId: string | null;
  currentJobType: string | null;
}

export interface PagedList<T> {
  totalCount: number;
  pageCount: number;
  items: T[];
}

export interface BulkResult {
  succeeded: number;
  skipped: number;
}
