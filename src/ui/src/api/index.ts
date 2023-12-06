import axios from "axios";

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

export interface ReturnedJobs {
	jobs: [{ count: number; jobType: JobType }];
	recurringJobs: number;
	batches: [{ count: number; batchType: BatchType }];
}

export async function getNavigationData(): Promise<ReturnedJobs> {
	const data = await axios
		.get("https://3ed2154c-0443-4a3a-8e18-2575262f4d21.mock.pstmn.io/naviationdata")
		.then(res => res.data);
	return data as ReturnedJobs;
}
