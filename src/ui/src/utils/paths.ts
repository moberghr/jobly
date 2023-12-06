const Routes = {
	dashboard: "/",
	jobs: "/jobs",
	recurringJobs: "/recurring-jobs",
	batches: "/batches",
};

export enum RouteTypes {
	enqueued = "enqueued",
	scheduled = "scheduled",
	processing = "processing",
	succeeded = "succeeded",
	failed = "failed",
	deleted = "deleted",
	awaiting = "awaiting",
}

export const JobsAndBatchesRoutesByType = Object.keys(RouteTypes)
	.map(key => {
		return [
			{
				mainRoute: Routes.jobs,
				path: `${Routes.jobs}/${key}`,
			},
			{
				mainRoute: Routes.batches,
				path: `${Routes.batches}/${key}`,
			},
		];
	})
	.flat();

export default Routes;
