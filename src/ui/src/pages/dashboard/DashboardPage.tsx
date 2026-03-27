import { useDashboardStore } from '@/stores/dashboard';
import { MetricCard } from '@/components/MetricCard';
import { Card, CardContent } from '@/components/ui/card';
import {
  Briefcase,
  CheckCircle,
  XCircle,
  Clock,
  Server,
  Mail,
  Loader,
  Hourglass,
  AlertTriangle,
} from 'lucide-react';

export default function DashboardPage() {
  const { stats, error } = useDashboardStore();

  if (error) {
    return (
      <div>
        <h1 className="text-2xl font-bold mb-6">Dashboard</h1>
        <Card>
          <CardContent className="py-12 text-center">
            <AlertTriangle className="h-10 w-10 text-destructive mx-auto mb-3" />
            <p className="text-lg font-medium">{error}</p>
            <p className="text-sm text-muted-foreground mt-1">
              Make sure the Jobly backend is running and accessible.
            </p>
          </CardContent>
        </Card>
      </div>
    );
  }

  if (!stats) {
    return (
      <div>
        <h1 className="text-2xl font-bold mb-6">Dashboard</h1>
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader className="h-4 w-4 animate-spin" />
          <span>Connecting to Jobly...</span>
        </div>
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-2xl font-bold mb-6">Dashboard</h1>
      {/* Live counts */}
      <h2 className="text-sm font-semibold text-muted-foreground uppercase mb-3">Current</h2>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <MetricCard label="Enqueued" value={stats.created} icon={<Briefcase className="h-5 w-5" />} />
        <MetricCard label="Processing" value={stats.processing} icon={<Loader className="h-5 w-5" />} color="text-purple-600" />
        <MetricCard label="Scheduled" value={stats.scheduled} icon={<Clock className="h-5 w-5" />} />
        <MetricCard label="Awaiting" value={stats.awaiting} icon={<Hourglass className="h-5 w-5" />} />
        <MetricCard label="Failed" value={stats.failed} icon={<XCircle className="h-5 w-5" />} color="text-red-600" />
        <MetricCard label="Messages" value={stats.messages} icon={<Mail className="h-5 w-5" />} />
        <MetricCard label="Servers" value={stats.servers} icon={<Server className="h-5 w-5" />} />
      </div>

      {/* Historical totals (survive job deletion) */}
      <h2 className="text-sm font-semibold text-muted-foreground uppercase mb-3">Historical</h2>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard label="Total Created" value={stats.totalCreated} icon={<Briefcase className="h-5 w-5" />} />
        <MetricCard label="Total Succeeded" value={stats.totalSucceeded} icon={<CheckCircle className="h-5 w-5" />} color="text-green-600" />
        <MetricCard label="Total Failed" value={stats.totalFailed} icon={<XCircle className="h-5 w-5" />} color="text-red-600" />
        <MetricCard label="Total Deleted" value={stats.totalDeleted} icon={<Briefcase className="h-5 w-5" />} />
      </div>
    </div>
  );
}
