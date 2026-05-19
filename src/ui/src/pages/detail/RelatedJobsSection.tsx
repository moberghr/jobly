import { FilteredJobsTable } from '@/components/FilteredJobsTable';
import type { UnifiedJobDetailModel, PagedList, JobModel } from '@/types';
import * as api from '@/api';

interface RelatedJobsSectionProps {
  job: UnifiedJobDetailModel;
  onCountsUpdate: (counts: Record<string, number>) => void;
}

export function RelatedJobsSection({ job, onCountsUpdate }: RelatedJobsSectionProps) {
  const isBatch = job.kind === 3;

  const fetchJobs = (page: number, pageSize: number, state?: string): Promise<PagedList<JobModel>> =>
    isBatch
      ? api.getBatchJobs(job.id, page, pageSize, state)
      : api.getMessageJobs(job.id, page, pageSize, state);

  const fetchCounts = () =>
    isBatch
      ? api.getBatchJobCounts(job.id)
      : api.getMessageJobCounts(job.id);

  return (
    <div className="mt-6">
      <FilteredJobsTable
        key={job.id}
        title="Jobs"
        parentId={job.id}
        parentKind={isBatch ? 'batch' : 'message'}
        fetchJobs={fetchJobs}
        fetchCounts={fetchCounts}
        onCountsUpdate={onCountsUpdate}
      />
    </div>
  );
}
