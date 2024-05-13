export interface IGetBatchesResponse {
    data: (StartedBatch | SucceededBatch | CompletedBatch | DeleteBatch | AwaitingBatch)[];
    totalCount: number;
}

export enum BatchType {
    started = "STARTED",
    succeeded = "SUCCEEDED",
    completed = "COMPLETED",
    awaiting = "AWAITING",
    deleted = "DELETED",
}

interface Batch {
    id: string;
    batch: string;
}

interface StartedBatch extends Batch {}

interface SucceededBatch extends Batch {}

interface CompletedBatch extends Batch {}

interface DeleteBatch extends Batch {}

interface AwaitingBatch extends Batch {}
