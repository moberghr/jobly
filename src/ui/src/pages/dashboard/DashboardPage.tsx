import { useDashboardStore } from '@/stores/dashboard';
import { MetricCard } from '@/components/MetricCard';
import {
  Briefcase,
  CheckCircle,
  XCircle,
  Clock,
  Server,
  Mail,
  Loader,
  Hourglass,
} from 'lucide-react';

export default function DashboardPage() {
  const { stats } = useDashboardStore();

  if (!stats) {
    return <div className="text-muted-foreground">Loading...</div>;
  }

  return (
    <div>
      <h1 className="text-2xl font-bold mb-6">Dashboard</h1>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard label="Enqueued" value={stats.created} icon={<Briefcase className="h-5 w-5" />} />
        <MetricCard label="Processing" value={stats.processing} icon={<Loader className="h-5 w-5" />} color="text-purple-600" />
        <MetricCard label="Completed" value={stats.completed} icon={<CheckCircle className="h-5 w-5" />} color="text-green-600" />
        <MetricCard label="Failed" value={stats.failed} icon={<XCircle className="h-5 w-5" />} color="text-red-600" />
        <MetricCard label="Scheduled" value={stats.scheduled} icon={<Clock className="h-5 w-5" />} />
        <MetricCard label="Awaiting" value={stats.awaiting} icon={<Hourglass className="h-5 w-5" />} />
        <MetricCard label="Messages" value={stats.messages} icon={<Mail className="h-5 w-5" />} />
        <MetricCard label="Servers" value={stats.servers} icon={<Server className="h-5 w-5" />} />
      </div>
    </div>
  );
}
