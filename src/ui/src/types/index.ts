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
  batches: number;
  databaseConnection: string | null;
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
  logs: JobLogModel[];
  siblingJobCount: number;
  childJobCount: number;
  traceId: string | null;
  spawnedByJobId: string | null;
  traceJobCount: number;
}

export interface JobLogModel {
  id: string;
  eventType: string;
  timestamp: string;
  level: string;
  message: string;
  exception: string | null;
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
  jobsCount: number;
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

export interface RecurringJobDetailModel extends RecurringJobModel {
  message: string | null;
  updatedAt: string | null;
  nextJobId: string | null;
  lastJobId: string | null;
  totalJobCount: number;
}

export interface ServerModel {
  id: string;
  serverName: string;
  startedTime: string;
  lastHeartbeatTime: string;
  serviceCount: number;
  cpuUsagePercent: number | null;
  memoryWorkingSetBytes: number | null;
  workers: WorkerModel[];
}

export interface ServerTaskSummary {
  taskName: string;
  lastStatus: string | null;
  lastMessage: string | null;
  lastRun: string | null;
  lastDurationMs: number | null;
  intervalSeconds: number | null;
}

export interface ServerLogModel {
  id: number;
  taskName: string;
  status: string;
  message: string | null;
  timestamp: string;
  durationMs: number | null;
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

export interface BatchModel {
  id: string;
  totalJobs: number;
  remainingJobs: number;
  placeholderState: State;
  createTime: string;
}

export interface BatchDetailModel extends BatchModel {
  continuationJobId: string | null;
}

export interface BulkResult {
  succeeded: number;
  skipped: number;
}

export interface StatsHistoryPoint {
  hour: string;
  succeeded: number;
  failed: number;
}
