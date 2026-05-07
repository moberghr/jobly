import { State, CancellationMode } from '@/types';
import type {
  DashboardStatistics,
  JobModel,
  JobGroupModel,
  RecurringJobModel,
  RecurringJobDetailModel,
  RecurringJobHistoryModel,
  ServerModel,
  ServerTaskSummary,
  ServerLogModel,
  WorkerDetailModel,
  WorkerJobLogModel,
  UnifiedJobDetailModel,
  JobLogModel,
  TraceJobModel,
  StatsHistoryPoint,
  TypeCountModel,
  PagedList,
} from '@/types';
import type { RealtimePoint } from '@/stores/dashboard';

// ============================================================
// Helpers
// ============================================================

const NOW = Date.now();

function ago(seconds: number): string {
  return new Date(NOW - seconds * 1000).toISOString();
}

function future(seconds: number): string {
  return new Date(NOW + seconds * 1000).toISOString();
}

/** Deterministic pseudo-random [0,1) from integer seed */
function seeded(n: number): number {
  const x = Math.sin(n * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
}

/** Deterministic UUID-looking string from a numeric seed */
function uid(n: number): string {
  const h = (v: number) =>
    ((v * 2654435761 + 1013904223) >>> 0).toString(16).padStart(8, '0');
  const a = h(n);
  const b = h(n + 7919);
  const c = h(n + 104729);
  return `${a}-${b.slice(0, 4)}-4${b.slice(5, 8)}-a${c.slice(1, 4)}-${c}${a.slice(0, 4)}`;
}

export function paginate<T>(
  items: T[],
  page = 0,
  pageSize = 20,
  totalOverride?: number,
): PagedList<T> {
  const total = totalOverride ?? items.length;
  return {
    totalCount: total,
    pageCount: Math.ceil(total / pageSize),
    items: items.slice(page * pageSize, (page + 1) * pageSize),
  };
}

// ============================================================
// Stable IDs (cross-referenced across endpoints)
// ============================================================

export const IDS = {
  server1: 'c7e3a1b4-5d8f-4e2a-9b6c-1d3e5f7a9b0d',
  server2: 'd8f4b2c5-6e9a-5f3b-0c7d-2e4f6a8b0c1e',
  worker1: 'a1b2c3d4-0001-4000-a000-000000000001',
  traceId: '4bf92f35-77b3-4da6-a3ce-929d0e0e4736',
  failedJob: 'e5f6a7b8-c9d0-4e1f-2a3b-4c5d6e7f8a9b',
  completedJobWithTrace: 'b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e',
  processingJob: 'f1e2d3c4-b5a6-4978-8a9b-c0d1e2f3a4b5',
  batch1: 'a9b8c7d6-e5f4-4321-0fed-cba987654321',
  message1: '12345678-abcd-4ef0-1234-567890abcdef',
  // Trace tree nodes
  trProcessOrder: 'a1b2c3d4-e5f6-4789-abcd-111111111111',
  trShipmentBatch: 'b2c3d4e5-f6a7-4890-bcde-222222222222',
  trShipItem1: 'c3d4e5f6-a7b8-4901-cdef-333333333301',
  trShipItem2: 'c3d4e5f6-a7b8-4901-cdef-333333333302',
  trShipItem3: 'c3d4e5f6-a7b8-4901-cdef-333333333303',
  trShipItem4: 'c3d4e5f6-a7b8-4901-cdef-333333333304',
  trShipItem5: 'c3d4e5f6-a7b8-4901-cdef-333333333305',
  trPublishInvoice: 'd4e5f6a7-b8c9-4012-defa-444444444444',
  trNotification: 'e5f6a7b8-c9d0-4123-efab-555555555555',
  trSendEmail: 'f6a7b8c9-d0e1-4234-fabc-666666666601',
  trNotifyCustomer: 'f6a7b8c9-d0e1-4234-fabc-666666666602',
  trCalculateTax: 'a7b8c9d0-e1f2-4345-abcd-777777777777',
  workerGroup1: 'wg-001-default',
  workerGroup2: 'wg-002-priority',
};

// ============================================================
// Job type pool
// ============================================================

const TYPES = [
  { type: 'Acme.Orders.ProcessOrderRequest', handler: 'Acme.Orders.ProcessOrderHandler' },
  { type: 'Acme.Shipping.ShipItemRequest', handler: 'Acme.Shipping.ShipItemHandler' },
  { type: 'Acme.Billing.PublishInvoiceRequest', handler: 'Acme.Billing.PublishInvoiceHandler' },
  { type: 'Acme.Notifications.SendEmailRequest', handler: 'Acme.Notifications.SendEmailCommand' },
  { type: 'Acme.Reports.GenerateReportRequest', handler: 'Acme.Reports.GenerateReportHandler' },
  { type: 'Acme.Inventory.SyncInventoryRequest', handler: 'Acme.Inventory.SyncInventoryHandler' },
  { type: 'Acme.Payments.ProcessPaymentRequest', handler: 'Acme.Payments.ProcessPaymentHandler' },
  { type: 'Acme.Notifications.NotifyCustomerRequest', handler: 'Acme.Notifications.NotifyCustomerHandler' },
  { type: 'Acme.Products.ImportProductsRequest', handler: 'Acme.Products.ImportProductsHandler' },
  { type: 'Acme.Billing.CalculateTaxRequest', handler: 'Acme.Billing.CalculateTaxHandler' },
];

function jobType(i: number) {
  return TYPES[i % TYPES.length];
}

// ============================================================
// Factory helpers
// ============================================================

function makeJob(seed: number, state: number, timeOffset: number, scheduled = false): JobModel {
  const t = jobType(seed);
  return {
    id: uid(seed + state * 1000 + (scheduled ? 7000 : 0)),
    type: t.type,
    message: JSON.stringify({ orderId: seed + 1000, customerId: `cust-${seed % 50}` }),
    createTime: ago(timeOffset),
    scheduleTime: scheduled ? future(seed * 600 + 300) : ago(timeOffset),
    processedTime:
      state === State.Completed || state === State.Failed || state === State.Deleted
        ? ago(timeOffset - 2)
        : null,
    currentState: state as typeof State.Enqueued,
    cancellationMode: CancellationMode.None,
    handlerType: t.handler,
  };
}

function makeJobs(count: number, state: number, baseOffset: number): JobModel[] {
  return Array.from({ length: count }, (_, i) =>
    makeJob(i, state, baseOffset + i * 30),
  );
}

function makeLog(
  id: string,
  eventType: string,
  secondsAgo: number,
  message: string | null,
  durationMs: number | null,
  exception: string | null = null,
  level = 'Information',
  workerId: string | null = null,
): JobLogModel {
  return {
    id,
    eventType,
    timestamp: ago(secondsAgo),
    level,
    message: message ?? '',
    exception,
    durationMs,
    workerId,
  };
}

// ============================================================
// Dashboard statistics (incrementing counters for realtime chart)
// ============================================================

let statusCallCount = 0;

export function getDashboardStats(): DashboardStatistics {
  statusCallCount++;
  const base = 15692;
  const added = statusCallCount * Math.round(15 + seeded(statusCallCount) * 8);
  const addedFailed = Math.floor(statusCallCount / 7);

  return {
    total: 15847 + added + addedFailed,
    pending: 23,
    scheduled: 12,
    created: 23,
    completed: base + added,
    failed: 47 + addedFailed,
    processing: 8,
    servers: 2,
    awaiting: 3,
    deleted: 62,
    batchesProcessing: 5,
    batchesAwaiting: 2,
    batchesDeleted: 1,
    batchesCompleted: 23,
    batchesFailed: 3,
    messagesEnqueued: 5,
    messagesProcessing: 3,
    messagesCompleted: 143,
    messagesFailed: 5,
    messages: 156,
    totalSucceeded: base + added,
    totalFailed: 47 + addedFailed,
    totalDeleted: 62,
    totalCreated: 15847 + added + addedFailed,
    batches: 34,
    databaseConnection: 'PostgreSQL',
  };
}

// ============================================================
// Stats history (24h / 7d)
// ============================================================

export function getStatsHistoryPoints(hours: number): StatsHistoryPoint[] {
  const now = new Date(NOW);
  now.setMinutes(0, 0, 0);

  return Array.from({ length: hours }, (_, i) => {
    const hourDate = new Date(now.getTime() - (hours - 1 - i) * 3600000);
    const h = hourDate.getHours();

    let base: number;
    if (h >= 9 && h <= 17) {
      base = 800 + seeded(i + 10) * 500;
    } else if (h >= 6 && h <= 8) {
      base = 200 + seeded(i + 20) * 400;
    } else if (h >= 18 && h <= 21) {
      base = 300 + seeded(i + 30) * 400;
    } else {
      base = 50 + seeded(i + 40) * 150;
    }

    return {
      hour: hourDate.toISOString(),
      succeeded: Math.round(base),
      failed: Math.round(base * (0.01 + seeded(i + 50) * 0.04)),
    };
  });
}

export function getCountersDemo() {
  return [
    { key: 'stats:succeeded', value: 15847 },
    { key: 'stats:failed', value: 47 },
    { key: 'stats:deleted', value: 62 },
    { key: 'stats:requeued', value: 18 },
  ];
}

// ============================================================
// Realtime chart seed (60 seconds of pre-populated data)
// ============================================================

export function generateRealtimeHistory(): RealtimePoint[] {
  const nowSec = Math.floor(NOW / 1000);
  return Array.from({ length: 60 }, (_, i) => ({
    ts: nowSec - (59 - i),
    succeeded: Math.round(15 + seeded(i) * 10 + Math.sin(i * 0.4) * 3),
    failed: seeded(i + 200) > 0.85 ? Math.round(1 + seeded(i + 300) * 2) : 0,
  }));
}

// ============================================================
// Job lists by state
// ============================================================

export const enqueuedJobs = makeJobs(23, State.Enqueued, 120);

export const processingJobs: JobModel[] = [
  {
    ...makeJob(0, State.Processing, 15),
    id: IDS.processingJob,
    type: 'Acme.Orders.ProcessOrderRequest',
    handlerType: 'Acme.Orders.ProcessOrderHandler',
  },
  ...makeJobs(7, State.Processing, 30).map((j, i) => ({ ...j, id: uid(i + 100) })),
];

export const scheduledJobs = Array.from({ length: 12 }, (_, i) =>
  makeJob(i, State.Enqueued, 120 + i * 30, true),
);

export const completedJobs: JobModel[] = [
  {
    ...makeJob(0, State.Completed, 180),
    id: IDS.completedJobWithTrace,
    type: 'Acme.Orders.ProcessOrderRequest',
    handlerType: 'Acme.Orders.ProcessOrderHandler',
  },
  ...makeJobs(19, State.Completed, 60),
];

export const failedJobs: JobModel[] = [
  {
    ...makeJob(0, State.Failed, 300),
    id: IDS.failedJob,
    type: 'Acme.Notifications.SendEmailRequest',
    handlerType: 'Acme.Notifications.SendEmailCommand',
  },
  ...Array.from({ length: 26 }, (_, i) => ({
    ...makeJob(i + 1, State.Failed, 300 + i * 30),
    type: 'Acme.Notifications.SendEmailRequest',
    handlerType: 'Acme.Notifications.SendEmailCommand',
  })),
  ...Array.from({ length: 12 }, (_, i) => ({
    ...makeJob(i + 30, State.Failed, 400 + i * 30),
    type: 'Acme.Payments.ProcessPaymentRequest',
    handlerType: 'Acme.Payments.ProcessPaymentHandler',
  })),
  ...Array.from({ length: 8 }, (_, i) => ({
    ...makeJob(i + 50, State.Failed, 500 + i * 30),
    type: 'Acme.Inventory.SyncInventoryRequest',
    handlerType: 'Acme.Inventory.SyncInventoryHandler',
  })),
];

export const awaitingJobs = makeJobs(3, State.Awaiting, 90);

export const deletedJobs = makeJobs(20, State.Deleted, 600);

// ============================================================
// Failed job type breakdown
// ============================================================

export const failedJobTypes: TypeCountModel[] = [
  { type: 'Acme.Notifications.SendEmailRequest', count: 27 },
  { type: 'Acme.Payments.ProcessPaymentRequest', count: 12 },
  { type: 'Acme.Inventory.SyncInventoryRequest', count: 8 },
];

// ============================================================
// Messages
// ============================================================

function makeMessage(
  seed: number,
  state: number,
  totalJobs: number,
  completed: number,
  failed: number,
): JobGroupModel {
  return {
    id: uid(seed + 5000),
    kind: 2,
    currentState: state as typeof State.Enqueued,
    jobCount: totalJobs,
    createTime: ago(300 + seed * 60),
    type: TYPES[seed % TYPES.length].type,
    payload: null,
    queue: seed % 3 === 0 ? 'high-priority' : 'default',
    totalJobs,
    completedJobs: completed,
    failedJobs: failed,
    continuationOptions: null,
  };
}

const messagesAll: JobGroupModel[] = [
  { ...makeMessage(0, State.Enqueued, 4, 0, 0), id: IDS.message1 },
  makeMessage(1, State.Enqueued, 3, 0, 0),
  makeMessage(2, State.Enqueued, 6, 0, 0),
  makeMessage(3, State.Enqueued, 2, 0, 0),
  makeMessage(4, State.Enqueued, 5, 0, 0),
  makeMessage(5, State.Processing, 8, 3, 0),
  makeMessage(6, State.Processing, 6, 2, 1),
  makeMessage(7, State.Processing, 10, 5, 0),
  ...Array.from({ length: 10 }, (_, i) =>
    makeMessage(10 + i, State.Completed, 4 + (i % 5), 4 + (i % 5), 0),
  ),
  ...Array.from({ length: 5 }, (_, i) =>
    makeMessage(30 + i, State.Failed, 5, 3, 2),
  ),
];

export function getMessages(state?: string): JobGroupModel[] {
  if (!state) {
    return messagesAll;
  }
  const stateMap: Record<string, number> = {
    enqueued: State.Enqueued,
    processing: State.Processing,
    completed: State.Completed,
    failed: State.Failed,
  };
  const s = stateMap[state];
  return s != null ? messagesAll.filter((m) => m.currentState === s) : messagesAll;
}

// ============================================================
// Batches
// ============================================================

function makeBatch(
  seed: number,
  state: number,
  totalJobs: number,
  completed: number,
  failed: number,
): JobGroupModel {
  return {
    id: uid(seed + 6000),
    kind: 3,
    currentState: state as typeof State.Enqueued,
    jobCount: totalJobs,
    createTime: ago(200 + seed * 45),
    type: null,
    payload: null,
    queue: 'default',
    totalJobs,
    completedJobs: completed,
    failedJobs: failed,
    continuationOptions: seed % 4 === 0 ? 1 : null,
  };
}

const batchesAll: JobGroupModel[] = [
  { ...makeBatch(0, State.Processing, 25, 18, 1), id: IDS.batch1 },
  makeBatch(1, State.Processing, 50, 35, 2),
  makeBatch(2, State.Processing, 10, 3, 0),
  makeBatch(3, State.Processing, 100, 72, 5),
  makeBatch(4, State.Processing, 8, 6, 0),
  makeBatch(5, State.Awaiting, 15, 0, 0),
  makeBatch(6, State.Awaiting, 20, 0, 0),
  ...Array.from({ length: 10 }, (_, i) =>
    makeBatch(10 + i, State.Completed, 20 + i * 5, 20 + i * 5, 0),
  ),
  ...Array.from({ length: 3 }, (_, i) =>
    makeBatch(25 + i, State.Failed, 30, 25, 5),
  ),
  makeBatch(30, State.Deleted, 10, 8, 2),
];

export function getBatches(state?: string): JobGroupModel[] {
  if (!state) {
    return batchesAll;
  }
  const stateMap: Record<string, number> = {
    processing: State.Processing,
    awaiting: State.Awaiting,
    completed: State.Completed,
    failed: State.Failed,
    deleted: State.Deleted,
  };
  const s = stateMap[state];
  return s != null ? batchesAll.filter((b) => b.currentState === s) : batchesAll;
}

// ============================================================
// Recurring jobs
// ============================================================

export const recurringJobs: RecurringJobModel[] = [
  {
    id: 1, name: 'Daily Report', cron: '0 8 * * *',
    type: 'Acme.Reports.GenerateReportRequest',
    nextExecution: future(3600), lastExecution: ago(82800), createdAt: ago(86400 * 30),
    disabledAt: null,
  },
  {
    id: 2, name: 'Inventory Sync', cron: '*/15 * * * *',
    type: 'Acme.Inventory.SyncInventoryRequest',
    nextExecution: future(600), lastExecution: ago(300), createdAt: ago(86400 * 60),
    disabledAt: null,
  },
  {
    id: 3, name: 'Email Digest', cron: '0 18 * * 1-5',
    type: 'Acme.Notifications.SendEmailRequest',
    nextExecution: future(86400), lastExecution: ago(86400), createdAt: ago(86400 * 90),
    disabledAt: ago(3600),
  },
  {
    id: 4, name: 'Tax Calculation', cron: '0 0 1 * *',
    type: 'Acme.Billing.CalculateTaxRequest',
    nextExecution: future(86400 * 15), lastExecution: ago(86400 * 15), createdAt: ago(86400 * 180),
    disabledAt: null,
  },
  {
    id: 5, name: 'Order Cleanup', cron: '0 3 * * *',
    type: 'Acme.Orders.ProcessOrderRequest',
    nextExecution: future(28800), lastExecution: ago(57600), createdAt: ago(86400 * 7),
    disabledAt: null,
  },
];

export function getRecurringDetail(id: number): RecurringJobDetailModel {
  const rj = recurringJobs.find((r) => r.id === id) ?? recurringJobs[0];
  return {
    ...rj,
    message: JSON.stringify({ filter: 'stale', maxAge: '30d' }),
    updatedAt: ago(86400 * 2),
  };
}

export function getRecurringHistory(id: number): RecurringJobHistoryModel[] {
  const rj = recurringJobs.find((r) => r.id === id);
  return Array.from({ length: 15 }, (_, i) => ({
    jobId: i < 12 ? uid(8000 + id * 100 + i) : null,
    createdAt: ago(i * 86400 + id * 3600),
    jobExists: i < 12,
    type: rj?.type ?? null,
    currentState: i < 12 ? (i === 3 ? State.Failed : State.Completed) : null,
    skipped: i >= 12 && i < 14,
  }));
}

// ============================================================
// Servers & workers
// ============================================================

function makeWorkers(serverId: string, count: number, startSeed: number): import('@/types').WorkerModel[] {
  return Array.from({ length: count }, (_, i) => ({
    workerId:
      i === 0 && serverId === IDS.server1
        ? IDS.worker1
        : uid(startSeed + i),
    startedTime: ago(7200 + i * 60),
    lastHeartbeatTime: ago(Math.round(seeded(startSeed + i) * 15)),
    currentJobId: i < 3 ? uid(9000 + startSeed + i) : null,
    currentJobType: i < 3 ? TYPES[i % TYPES.length].type : null,
    queues: i < 5 ? 'default' : 'default,high-priority',
    pollingIntervalMs: 1000,
    workerGroupId: i < 5 ? IDS.workerGroup1 : IDS.workerGroup2,
    workerGroupPausedAt: null,
  }));
}

export const servers: ServerModel[] = [
  {
    id: IDS.server1,
    serverName: 'warp-prod-server-1',
    startedTime: ago(7200),
    lastHeartbeatTime: ago(3),
    serviceCount: 10,
    cpuUsagePercent: 12.4,
    memoryWorkingSetBytes: 287000000,
    pausedAt: null,
    workers: makeWorkers(IDS.server1, 10, 200),
  },
  {
    id: IDS.server2,
    serverName: 'warp-prod-server-2',
    startedTime: ago(3600),
    lastHeartbeatTime: ago(5),
    serviceCount: 5,
    cpuUsagePercent: 4.8,
    memoryWorkingSetBytes: 142000000,
    pausedAt: null,
    workers: makeWorkers(IDS.server2, 5, 300),
  },
];

// ============================================================
// Server tasks & logs
// ============================================================

export const serverTasks: ServerTaskSummary[] = [
  { taskName: 'HeartbeatTask', lastStatus: 'Completed', lastMessage: 'Heartbeat sent', lastRun: ago(10), lastDurationMs: 12, intervalSeconds: 15 },
  { taskName: 'CounterAggregatorTask', lastStatus: 'Completed', lastMessage: 'Aggregated 847 counters', lastRun: ago(28), lastDurationMs: 145, intervalSeconds: 60 },
  { taskName: 'MessageRoutingTask', lastStatus: 'Completed', lastMessage: 'Routed 3 messages', lastRun: ago(2), lastDurationMs: 34, intervalSeconds: 1 },
  { taskName: 'OrchestrationTask', lastStatus: 'Completed', lastMessage: 'Finalized 2 batches', lastRun: ago(5), lastDurationMs: 67, intervalSeconds: 10 },
  { taskName: 'StaleJobRecoveryTask', lastStatus: 'Completed', lastMessage: 'No stale jobs', lastRun: ago(120), lastDurationMs: 8, intervalSeconds: 300 },
  { taskName: 'ExpirationCleanupTask', lastStatus: 'Completed', lastMessage: 'Deleted 156 expired jobs', lastRun: ago(60), lastDurationMs: 892, intervalSeconds: 300 },
  { taskName: 'RecurringJobSchedulerTask', lastStatus: 'Completed', lastMessage: 'Scheduled 1 job', lastRun: ago(45), lastDurationMs: 23, intervalSeconds: 60 },
  { taskName: 'ServerCleanupTask', lastStatus: 'Completed', lastMessage: 'No stale servers', lastRun: ago(300), lastDurationMs: 5, intervalSeconds: 600 },
];

export function getServerLogs(taskName?: string): ServerLogModel[] {
  const tasks = taskName ? [taskName] : serverTasks.map((t) => t.taskName);
  const logs: ServerLogModel[] = [];
  let logId = 1;
  for (const task of tasks) {
    for (let i = 0; i < 10; i++) {
      const isWarning = i === 4 && task === 'ExpirationCleanupTask';
      logs.push({
        id: logId++,
        taskName: task,
        status: isWarning ? 'Warning' : 'Completed',
        message: isWarning
          ? 'Lock contention on warp.jobs, retrying...'
          : `${task} executed successfully`,
        timestamp: ago(i * 60 + tasks.indexOf(task) * 10),
        durationMs: Math.round(10 + seeded(logId) * 200),
      });
    }
  }

  return logs.sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

// ============================================================
// Worker detail & logs
// ============================================================

export function getWorkerDetail(workerId: string): WorkerDetailModel {
  for (const server of servers) {
    const worker = server.workers.find((w) => w.workerId === workerId);
    if (worker) {
      return {
        workerId: worker.workerId,
        startedTime: worker.startedTime,
        lastHeartbeatTime: worker.lastHeartbeatTime,
        currentJobId: worker.currentJobId,
        currentJobType: worker.currentJobType,
        serverId: server.id,
        serverName: server.serverName,
        queues: worker.queues,
        pollingIntervalMs: worker.pollingIntervalMs,
        serverPausedAt: server.pausedAt,
        workerGroupId: worker.workerGroupId,
        workerGroupPausedAt: worker.workerGroupPausedAt,
      };
    }
  }

  return {
    workerId,
    startedTime: ago(7200),
    lastHeartbeatTime: ago(5),
    currentJobId: null,
    currentJobType: null,
    serverId: IDS.server1,
    serverName: 'warp-prod-server-1',
    queues: 'default',
    pollingIntervalMs: 1000,
    serverPausedAt: null,
    workerGroupId: null,
    workerGroupPausedAt: null,
  };
}

export function getWorkerLogs(): WorkerJobLogModel[] {
  return Array.from({ length: 25 }, (_, i) => {
    const eventType =
      i % 5 === 4 ? 'Failed' : i % 2 === 0 ? 'Processing' : 'Completed';
    const t = TYPES[i % TYPES.length];
    const dur = Math.round(50 + seeded(i) * 500);
    return {
      id: uid(7000 + i),
      jobId: uid(7500 + i),
      jobType: t.type,
      eventType,
      timestamp: ago(i * 45 + 10),
      level: eventType === 'Failed' ? 'Error' : 'Information',
      message:
        eventType === 'Failed'
          ? 'System.TimeoutException: The operation has timed out.'
          : eventType === 'Processing'
            ? 'Job started'
            : `Completed in ${dur}ms`,
      exception:
        eventType === 'Failed'
          ? [
              'System.TimeoutException: The operation has timed out.',
              '   at Acme.Notifications.SmtpEmailClient.SendAsync(EmailMessage msg, CancellationToken ct)',
              '   at Acme.Notifications.SendEmailCommand.HandleAsync(SendEmailRequest request, CancellationToken ct)',
              '   at Warp.Worker.WarpWorkerService.ExecuteJobAsync(Job job, CancellationToken ct)',
            ].join('\n')
          : null,
      durationMs: eventType === 'Completed' ? dur : null,
    };
  });
}

// ============================================================
// Job detail — failed job
// ============================================================

export const jobDetailFailed: UnifiedJobDetailModel = {
  id: IDS.failedJob,
  kind: 1,
  type: 'Acme.Notifications.SendEmailRequest',
  currentState: State.Failed,
  createTime: ago(300),
  cancellationMode: CancellationMode.None,
  message: JSON.stringify({
    to: 'customer@example.com',
    subject: 'Order Confirmation #1042',
    template: 'order-confirmation',
    orderId: 1042,
  }),
  handlerType: 'Acme.Notifications.SendEmailCommand',
  scheduleTime: ago(300),
  retriedTimes: 3,
  maxRetries: 3,
  totalJobs: 0,
  completedJobs: 0,
  failedJobs: 0,
  continuationOptions: null,
  queue: 'default',
  traceId: null,
  parentJob: null,
  spawnedByJob: null,
  continuations: [],
  spawnedJobs: [],
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  metadata: { correlationId: 'order-1042', source: 'OrderService', MaxRetries: 3, RetriedTimes: 3, RetryDelays: [15, 60, 300] } as any,
  logs: [
    makeLog('log-f1', 'Created', 300, null, null),
    makeLog('log-f2', 'Processing', 299, null, null, null, 'Information', IDS.worker1),
    makeLog(
      'log-f3', 'Failed', 297,
      'System.Net.Sockets.SocketException: Connection refused',
      1850,
      [
        'System.Net.Sockets.SocketException: Connection refused',
        '   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error)',
        '   at System.Net.Sockets.Socket.ConnectAsync(EndPoint remoteEP, CancellationToken ct)',
        '   at Acme.Notifications.SmtpEmailClient.SendAsync(EmailMessage msg, CancellationToken ct)',
        '   at Acme.Notifications.SendEmailCommand.HandleAsync(SendEmailRequest request, CancellationToken ct)',
      ].join('\n'),
      'Error', IDS.worker1,
    ),
    makeLog('log-f4', 'Requeued', 250, 'Retry 1/3', null),
    makeLog('log-f5', 'Processing', 249, null, null, null, 'Information', IDS.worker1),
    makeLog('log-f6', 'Failed', 247, 'System.Net.Sockets.SocketException: Connection refused', 1720, null, 'Error', IDS.worker1),
    makeLog('log-f7', 'Requeued', 200, 'Retry 2/3', null),
    makeLog('log-f8', 'Processing', 199, null, null, null, 'Information', IDS.worker1),
    makeLog('log-f9', 'Failed', 197, 'System.Net.Sockets.SocketException: Connection refused', 1650, null, 'Error', IDS.worker1),
    makeLog('log-f10', 'Requeued', 150, 'Retry 3/3', null),
    makeLog('log-f11', 'Processing', 149, null, null, null, 'Information', IDS.worker1),
    makeLog('log-f12', 'Failed', 147, 'System.Net.Sockets.SocketException: Connection refused', 1580, null, 'Error', IDS.worker1),
  ],
};

// ============================================================
// Job detail — completed job with trace
// ============================================================

export const jobDetailCompleted: UnifiedJobDetailModel = {
  id: IDS.completedJobWithTrace,
  kind: 1,
  type: 'Acme.Orders.ProcessOrderRequest',
  currentState: State.Completed,
  createTime: ago(600),
  cancellationMode: CancellationMode.None,
  message: JSON.stringify({
    orderId: 2847,
    items: [
      { sku: 'WIDGET-001', qty: 3, price: 29.99 },
      { sku: 'GADGET-002', qty: 1, price: 149.99 },
    ],
    customerId: 'cust-42',
    shippingMethod: 'express',
  }),
  handlerType: 'Acme.Orders.ProcessOrderHandler',
  scheduleTime: ago(600),
  retriedTimes: 0,
  maxRetries: 3,
  totalJobs: 0,
  completedJobs: 0,
  failedJobs: 0,
  continuationOptions: null,
  queue: 'default',
  traceId: IDS.traceId,
  parentJob: null,
  spawnedByJob: null,
  continuations: [
    { id: IDS.trShipmentBatch, kind: 3, currentState: State.Completed, type: null, handlerType: null },
  ],
  spawnedJobs: [
    { id: IDS.trCalculateTax, kind: 1, currentState: State.Completed, type: 'Acme.Billing.CalculateTaxRequest', handlerType: 'Acme.Billing.CalculateTaxHandler' },
  ],
  metadata: { correlationId: 'order-2847', source: 'WebApp', priority: 'high' },
  logs: [
    makeLog('log-c1', 'Created', 600, null, null),
    makeLog('log-c2', 'Processing', 599, null, null, null, 'Information', IDS.worker1),
    makeLog('log-c3', 'Log', 598, 'Validating order #2847...', null, null, 'Information', IDS.worker1),
    makeLog('log-c4', 'Log', 597, 'Order validated successfully. Processing payment...', null, null, 'Information', IDS.worker1),
    makeLog('log-c5', 'Log', 596, 'Payment of $239.96 processed via Stripe.', null, null, 'Information', IDS.worker1),
    makeLog('log-c6', 'Log', 595, 'Creating shipment batch for 2 items...', null, null, 'Information', IDS.worker1),
    makeLog('log-c7', 'Log', 594, 'Spawning tax calculation job.', null, null, 'Information', IDS.worker1),
    makeLog('log-c8', 'Completed', 593, null, 4250, null, 'Information', IDS.worker1),
  ],
};

// ============================================================
// Batch detail (unified format for /detail/{id})
// ============================================================

export const batchDetailUnified: UnifiedJobDetailModel = {
  id: IDS.batch1,
  kind: 3,
  type: null,
  currentState: State.Processing,
  createTime: ago(590),
  cancellationMode: CancellationMode.None,
  message: null,
  handlerType: null,
  scheduleTime: null,
  retriedTimes: 0,
  maxRetries: 0,
  totalJobs: 25,
  completedJobs: 18,
  failedJobs: 1,
  continuationOptions: 1,
  queue: 'default',
  traceId: IDS.traceId,
  parentJob: {
    id: IDS.completedJobWithTrace,
    kind: 1,
    currentState: State.Completed,
    type: 'Acme.Orders.ProcessOrderRequest',
    handlerType: 'Acme.Orders.ProcessOrderHandler',
  },
  spawnedByJob: null,
  continuations: [
    { id: IDS.trPublishInvoice, kind: 1, currentState: State.Awaiting, type: 'Acme.Billing.PublishInvoiceRequest', handlerType: 'Acme.Billing.PublishInvoiceHandler' },
  ],
  spawnedJobs: [],
  metadata: null,
  logs: [
    makeLog('log-b1', 'Created', 590, null, null),
    makeLog('log-b2', 'Processing', 589, '25 child jobs created', null),
  ],
};

const batchChildJobs: JobModel[] = Array.from({ length: 25 }, (_, i) => ({
  id: uid(4000 + i),
  type: 'Acme.Shipping.ShipItemRequest',
  message: JSON.stringify({ itemId: `ITEM-${1000 + i}`, destination: 'Warehouse B' }),
  createTime: ago(580 - i * 2),
  scheduleTime: ago(580 - i * 2),
  processedTime:
    i < 18
      ? ago(570 - i * 2)
      : i === 23
        ? ago(550)
        : null,
  currentState:
    i < 18
      ? State.Completed
      : i === 23
        ? State.Failed
        : i < 21
          ? State.Processing
          : State.Enqueued,
  cancellationMode: CancellationMode.None,
  handlerType: 'Acme.Shipping.ShipItemHandler',
}));

export const batchJobCounts: Record<string, number> = {
  enqueued: 2,
  processing: 4,
  completed: 18,
  failed: 1,
  awaiting: 0,
  deleted: 0,
};

export function getBatchChildren(state?: string): JobModel[] {
  if (!state) {
    return batchChildJobs;
  }
  const stateMap: Record<string, number> = {
    enqueued: State.Enqueued,
    processing: State.Processing,
    completed: State.Completed,
    failed: State.Failed,
    awaiting: State.Awaiting,
    deleted: State.Deleted,
  };
  const s = stateMap[state];
  return s != null ? batchChildJobs.filter((j) => j.currentState === s) : batchChildJobs;
}

// ============================================================
// Message detail (unified format for /detail/{id})
// ============================================================

export const messageDetailUnified: UnifiedJobDetailModel = {
  id: IDS.message1,
  kind: 2,
  type: 'Acme.Notifications.SendEmailRequest',
  currentState: State.Processing,
  createTime: ago(410),
  cancellationMode: CancellationMode.None,
  message: JSON.stringify({ subject: 'Weekly Digest', campaign: 'weekly-2026-w15' }),
  handlerType: null,
  scheduleTime: null,
  retriedTimes: 0,
  maxRetries: 0,
  totalJobs: 4,
  completedJobs: 3,
  failedJobs: 0,
  continuationOptions: null,
  queue: 'default',
  traceId: null,
  parentJob: null,
  spawnedByJob: null,
  continuations: [],
  spawnedJobs: [],
  metadata: null,
  logs: [
    makeLog('log-m1', 'Created', 410, null, null),
    makeLog('log-m2', 'Processing', 409, '4 handler jobs created', null),
  ],
};

const messageChildJobs: JobModel[] = Array.from({ length: 4 }, (_, i) => ({
  id: uid(4500 + i),
  type:
    i < 2
      ? 'Acme.Notifications.SendEmailRequest'
      : 'Acme.Notifications.NotifyCustomerRequest',
  message: JSON.stringify({ recipientId: `user-${100 + i}` }),
  createTime: ago(400 - i * 5),
  scheduleTime: ago(400 - i * 5),
  processedTime: i < 3 ? ago(395 - i * 5) : null,
  currentState: i < 3 ? State.Completed : State.Processing,
  cancellationMode: CancellationMode.None,
  handlerType:
    i < 2
      ? 'Acme.Notifications.SendEmailCommand'
      : 'Acme.Notifications.NotifyCustomerHandler',
}));

export const messageJobCounts: Record<string, number> = {
  enqueued: 0,
  processing: 1,
  completed: 3,
  failed: 0,
  awaiting: 0,
  deleted: 0,
};

export function getMessageChildren(state?: string): JobModel[] {
  if (!state) {
    return messageChildJobs;
  }
  const stateMap: Record<string, number> = {
    enqueued: State.Enqueued,
    processing: State.Processing,
    completed: State.Completed,
    failed: State.Failed,
    awaiting: State.Awaiting,
    deleted: State.Deleted,
  };
  const s = stateMap[state];
  return s != null ? messageChildJobs.filter((j) => j.currentState === s) : messageChildJobs;
}

// ============================================================
// Trace tree
// ============================================================

export const traceJobs: TraceJobModel[] = [
  { id: IDS.trProcessOrder, kind: 1, type: 'Acme.Orders.ProcessOrderRequest', handlerType: 'Acme.Orders.ProcessOrderHandler', currentState: State.Completed, parentJobId: null, spawnedByJobId: null, createTime: ago(600) },
  { id: IDS.trShipmentBatch, kind: 3, type: null, handlerType: null, currentState: State.Completed, parentJobId: IDS.trProcessOrder, spawnedByJobId: null, createTime: ago(595) },
  { id: IDS.trShipItem1, kind: 1, type: 'Acme.Shipping.ShipItemRequest', handlerType: 'Acme.Shipping.ShipItemHandler', currentState: State.Completed, parentJobId: IDS.trShipmentBatch, spawnedByJobId: null, createTime: ago(594) },
  { id: IDS.trShipItem2, kind: 1, type: 'Acme.Shipping.ShipItemRequest', handlerType: 'Acme.Shipping.ShipItemHandler', currentState: State.Completed, parentJobId: IDS.trShipmentBatch, spawnedByJobId: null, createTime: ago(593) },
  { id: IDS.trShipItem3, kind: 1, type: 'Acme.Shipping.ShipItemRequest', handlerType: 'Acme.Shipping.ShipItemHandler', currentState: State.Completed, parentJobId: IDS.trShipmentBatch, spawnedByJobId: null, createTime: ago(592) },
  { id: IDS.trShipItem4, kind: 1, type: 'Acme.Shipping.ShipItemRequest', handlerType: 'Acme.Shipping.ShipItemHandler', currentState: State.Completed, parentJobId: IDS.trShipmentBatch, spawnedByJobId: null, createTime: ago(591) },
  { id: IDS.trShipItem5, kind: 1, type: 'Acme.Shipping.ShipItemRequest', handlerType: 'Acme.Shipping.ShipItemHandler', currentState: State.Completed, parentJobId: IDS.trShipmentBatch, spawnedByJobId: null, createTime: ago(590) },
  { id: IDS.trPublishInvoice, kind: 1, type: 'Acme.Billing.PublishInvoiceRequest', handlerType: 'Acme.Billing.PublishInvoiceHandler', currentState: State.Completed, parentJobId: null, spawnedByJobId: IDS.trShipItem1, createTime: ago(585) },
  { id: IDS.trNotification, kind: 2, type: 'Acme.Notifications.InvoiceNotification', handlerType: null, currentState: State.Completed, parentJobId: null, spawnedByJobId: IDS.trPublishInvoice, createTime: ago(580) },
  { id: IDS.trSendEmail, kind: 1, type: 'Acme.Notifications.SendEmailRequest', handlerType: 'Acme.Notifications.SendEmailCommand', currentState: State.Completed, parentJobId: IDS.trNotification, spawnedByJobId: null, createTime: ago(579) },
  { id: IDS.trNotifyCustomer, kind: 1, type: 'Acme.Notifications.NotifyCustomerRequest', handlerType: 'Acme.Notifications.NotifyCustomerHandler', currentState: State.Completed, parentJobId: IDS.trNotification, spawnedByJobId: null, createTime: ago(578) },
  { id: IDS.trCalculateTax, kind: 1, type: 'Acme.Billing.CalculateTaxRequest', handlerType: 'Acme.Billing.CalculateTaxHandler', currentState: State.Completed, parentJobId: null, spawnedByJobId: IDS.trProcessOrder, createTime: ago(598) },
];
