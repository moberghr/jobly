import axios from "axios";
const API_URL_Mock = "https://3ed2154c-0443-4a3a-8e18-2575262f4d21.mock.pstmn.io";

enum JobType {
	enqueded = 1,
	scheduled = 2,
	processing = 3,
	succeeded = 4,
	failed = 5,
	deleted = 6,
	awaiting = 7,
}

enum BatchType {
	enqueded = 1,
	scheduled = 2,
	processing = 3,
	succeeded = 4,
	failed = 5,
	deleted = 6,
	awaiting = 7,
}

export interface ResponseJobs {
	jobs: [{ count: number; jobType: JobType }];
	recurringJobs: number;
	batches: [{ count: number; batchType: BatchType }];
}

export interface ResponseRecurringJobTableData {
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

export async function getNavigationData(): Promise<ResponseJobs | undefined> {
	const data = await axios.get(`${API_URL_Mock}/naviationdata`).then(res => res.data);
	return data as ResponseJobs;
}

export async function getRecurringJobTableData(page: number, pageSize: number): Promise<ResponseRecurringJobTableData> {
	const data = await axios.get(`${API_URL_Mock}/tableData`).then(res => res.data);
	return { data: data, totalCount: data.length } as ResponseRecurringJobTableData;
}
