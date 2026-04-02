export const State = {
  Enqueued: 1,
  Awaiting: 2,
  Processing: 3,
  Completed: 4,
  Failed: 5,
  Deleted: 6,
} as const;
export type State = (typeof State)[keyof typeof State];

export const CancellationMode = {
  None: 0,
  Graceful: 1,
} as const;
export type CancellationMode = (typeof CancellationMode)[keyof typeof CancellationMode];

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
  deleted: number;
  batchesActive: number;
  batchesCompleted: number;
  batchesFailed: number;
  messagesEnqueued: number;
  messagesProcessing: number;
  messagesCompleted: number;
  messagesFailed: number;
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
  cancellationMode: CancellationMode;
  handlerType: string | null;
}

export interface JobDetailModel extends JobModel {
  kind: number;
  parentJobId: string | null;
  retriedTimes: number;
  maxRetries: number;
  logs: JobLogModel[];
  siblingJobCount: number;
  childJobCount: number;
  traceId: string | null;
  spawnedByJobId: string | null;
  traceJobCount: number;
  concurrencyKey: string | null;
  continuations: ContinuationInfo[];
}

export interface JobLogModel {
  id: string;
  eventType: string;
  timestamp: string;
  level: string;
  message: string;
  exception: string | null;
  durationMs: number | null;
  workerId: string | null;
}

export interface JobGroupModel {
  id: string;
  kind: number;
  currentState: State;
  jobCount: number;
  createTime: string;
  type: string | null;
  payload: string | null;
  queue: string | null;
  totalJobs: number;
  completedJobs: number;
  failedJobs: number;
  continuationOptions: number | null;
}

export interface ContinuationInfo {
  id: string;
  kind: number;
  currentState: State;
  type: string | null;
  handlerType: string | null;
}

export interface JobGroupDetailModel extends JobGroupModel {
  parentJobId: string | null;
  parentJobKind: number | null;
  spawnedJobsCount: number;
  continuations: ContinuationInfo[];
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
}

export interface RecurringJobHistoryModel {
  jobId: string | null;
  createdAt: string;
  jobExists: boolean;
  type: string | null;
  currentState: State | null;
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
  queues: string | null;
  pollingIntervalMs: number | null;
}

export interface PagedList<T> {
  totalCount: number;
  pageCount: number;
  items: T[];
}

// BatchModel and MessageModel are now unified as JobGroupModel above

export interface BulkResult {
  succeeded: number;
  skipped: number;
}

export interface WorkerDetailModel {
  workerId: string;
  startedTime: string;
  lastHeartbeatTime: string | null;
  currentJobId: string | null;
  currentJobType: string | null;
  serverId: string;
  serverName: string;
  queues: string | null;
  pollingIntervalMs: number | null;
}

export interface WorkerJobLogModel {
  id: string;
  jobId: string;
  jobType: string | null;
  eventType: string;
  timestamp: string;
  level: string;
  message: string;
  exception: string | null;
  durationMs: number | null;
}

export interface TypeCountModel {
  type: string;
  count: number;
}

export interface StatsHistoryPoint {
  hour: string;
  succeeded: number;
  failed: number;
}
