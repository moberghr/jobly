export interface IGetRecurringJobsTableResponse {
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
