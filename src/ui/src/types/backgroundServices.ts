// Enums — numeric values match C# enum assignments (always start at 1 per project convention)
export const ServiceScope = {
  PerServer: 1,
  Singleton: 2,
} as const;
export type ServiceScope = (typeof ServiceScope)[keyof typeof ServiceScope];

export const BackgroundServiceStatus = {
  Running: 1,
  Waiting: 2,
  Faulted: 3,
  Restarting: 4,
  ConfigurationMismatch: 5,
} as const;
export type BackgroundServiceStatus = (typeof BackgroundServiceStatus)[keyof typeof BackgroundServiceStatus];

export const BackgroundServiceLogSource = {
  Lifecycle: 1,
  User: 2,
} as const;
export type BackgroundServiceLogSource = (typeof BackgroundServiceLogSource)[keyof typeof BackgroundServiceLogSource];

// Microsoft.Extensions.Logging.LogLevel — numeric, starts at 0 per .NET convention
export const LogLevel = {
  Trace: 0,
  Debug: 1,
  Information: 2,
  Warning: 3,
  Error: 4,
  Critical: 5,
  None: 6,
} as const;
export type LogLevel = (typeof LogLevel)[keyof typeof LogLevel];

// DTOs — camelCase mirrors of C# DTOs from BackgroundServices/
export interface BackgroundServiceListItem {
  name: string;
  scope: ServiceScope;
  runningCount: number;
  waitingCount: number;
  faultedCount: number;
  configurationMismatchCount: number;
  totalInstances: number;
  totalRestartCount: number;
  lastErrorType: string | null;
}

export interface BackgroundServiceInstance {
  serverId: string;
  serverName: string | null;
  serviceName: string;
  declaredScope: ServiceScope;
  status: BackgroundServiceStatus;
  startedAt: string;
  lastHeartbeatAt: string;
  lastError: string | null;
  lastErrorAt: string | null;
  restartCount: number;
}

export interface BackgroundServiceDetail {
  name: string;
  declaredScope: ServiceScope;
  firstSeenAt: string;
  lastSeenAt: string;
  instances: BackgroundServiceInstance[];
}

export interface BackgroundServiceLeaseDto {
  serviceName: string;
  holderServerId: string;
  holderServerName: string | null;
  leaseExpiresAt: string;
}

export interface BackgroundServiceLogDto {
  id: number;
  serverId: string;
  serverName: string | null;
  serviceName: string;
  timestamp: string;
  level: LogLevel;
  source: BackgroundServiceLogSource;
  message: string;
  exceptionType: string | null;
  exceptionMessage: string | null;
}
