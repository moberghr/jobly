import { BatchType, JobType } from "../utils/enums";

export interface IGetNavigationCountResponse {
    jobs: [{ count: number; jobType: JobType }];
    recurringJobs: number;
    batches: [{ count: number; batchType: BatchType }];
}
