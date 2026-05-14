import { FilteredJobsTable } from '@/components/FilteredJobsTable';
import type { UnifiedJobDetailModel, PagedList, JobModel } from '@/types';
import * as api from '@/api';

interface RelatedJobsSectionProps {
  job: UnifiedJobDetailModel;
  onCountsUpdate: (counts: Record<string, number>) => void;
}

export function RelatedJobsSection({ job, onCountsUpdate }: RelatedJobsSectionProps) {
  const fetchJobs = (page: number, pageSize: number, state?: string): Promise<PagedList<JobModel>> =>
    job.kind === 3
      ? api.getBatchJobs(job.id, page, pageSize, state)
      : api.getMessageJobs(job.id, page, pageSize, state);

  const fetchCounts = () =>
    job.kind === 3
      ? api.getBatchJobCounts(job.id)
      : api.getMessageJobCounts(job.id);

  return (
    <div className="mt-6">
      <FilteredJobsTable
        key={job.id}
        title="Jobs"
        fetchJobs={fetchJobs}
        fetchCounts={fetchCounts}
        onCountsUpdate={onCountsUpdate}
      />
    </div>
  );
}
