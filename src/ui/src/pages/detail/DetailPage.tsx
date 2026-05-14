import { useCallback, useState } from 'react';
import { useParams } from 'react-router-dom';
import { ErrorState } from '@/components/PageState';
import { DetailSkeleton } from '@/components/skeletons/DetailSkeleton';
import { useJobDetail } from '@/api/hooks/useJobs';
import { JobHeader } from './JobHeader';
import { JobTimeline } from './JobTimeline';
import { JobProgress } from './JobProgress';
import { JobLogs } from './JobLogs';
import { JobMetadata } from './JobMetadata';
import { RelatedJobsSection } from './RelatedJobsSection';

export default function DetailPage() {
  const { id } = useParams<{ id: string }>();
  const [jobCounts, setJobCounts] = useState<Record<string, number>>({});

  const query = useJobDetail(id);

  const handleCountsUpdate = useCallback((counts: Record<string, number>) => {
    setJobCounts(counts);
  }, []);

  if (query.error) return <ErrorState message={(query.error as Error).message} />;
  if (!query.data) return <DetailSkeleton />;

  const job = query.data;
  const systemEvents = job.logs.filter(l => l.eventType !== 'Log' && l.eventType !== 'Progress').reverse();
  const handlerLogs = job.logs.filter(l => l.eventType === 'Log');

  // Reported progress: latest value per bar name (append-only with dedup-on-no-change).
  const progressByName = new Map<string, number>();
  for (const log of job.logs) {
    if (log.eventType !== 'Progress' || log.value == null) continue;
    progressByName.set(log.name ?? '', log.value);
  }
  const reportedBars = Array.from(progressByName.entries());

  const totalJobs = Object.keys(jobCounts).length > 0
    ? Object.values(jobCounts).reduce((a, b) => a + b, 0)
    : job.totalJobs;
  const completedJobs = jobCounts['completed'] ?? job.completedJobs;
  const failedJobs = jobCounts['failed'] ?? job.failedJobs;

  const hasChildJobs = job.kind === 2 || job.kind === 3;

  return (
    <div>
      <JobHeader job={job} />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="space-y-4">
          <JobProgress jobId={job.id} batch={{ totalJobs, completedJobs, failedJobs }} />
          <JobMetadata job={job} />
        </div>
        <div className="space-y-4">
          <JobProgress jobId={job.id} reportedBars={reportedBars} />
          <JobTimeline jobId={job.id} events={systemEvents} />
          <JobLogs jobId={job.id} logs={handlerLogs} />
        </div>
      </div>
      {hasChildJobs && <RelatedJobsSection job={job} onCountsUpdate={handleCountsUpdate} />}
    </div>
  );
}
