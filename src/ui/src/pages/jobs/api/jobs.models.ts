export interface IGetJobsResponse {
    data: {
        id: string;
        cron: string;
        timeZone: string;
        job: string;
        nextExecution: string;
        lastExecution: {
            value: string;
            failed: boolean;
        };
    }[];
    totalCount: number;
}

export enum JobType {
    enqueued = "ENQUEUED",
    scheduled = "SCHEDULED",
    processing = "PROCESSING",
    Succeeded = "SUCCEEDED",
    failed = "FAILED",
    deleted = "DELETED",
    awaiting = "AWAITING",
}
