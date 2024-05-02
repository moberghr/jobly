export interface IGetJobsResponse {
    data: (EnqueuedJob | ScheduledJob | ProcessingJob | SucceededJob | FailedJob | DeleteJob | AwaitingJob)[];
    totalCount: number;
}

export enum JobType {
    enqueued = "ENQUEUED",
    scheduled = "SCHEDULED",
    processing = "PROCESSING",
    succeeded = "SUCCEEDED",
    failed = "FAILED",
    deleted = "DELETED",
    awaiting = "AWAITING",
}

interface Job {
    id: string;
    job: string;
}

interface EnqueuedJob extends Job {}

interface ScheduledJob extends Job {}

interface ProcessingJob extends Job {}

interface SucceededJob extends Job {}

interface FailedJob extends Job {
    cron: string;
    timeZone: string;
    nextExecution: string;
    lastExecution: {
        value: string;
        failed: boolean;
    };
}

interface DeleteJob extends Job {}

interface AwaitingJob extends Job {}
