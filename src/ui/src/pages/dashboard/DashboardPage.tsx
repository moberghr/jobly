import { useState, useEffect, useRef } from 'react';
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { useDashboardStore } from '@/stores/dashboard';
import { MetricCard } from '@/components/MetricCard';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { getStatsHistory } from '@/api';
import type { StatsHistoryPoint } from '@/types';
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

  // Realtime graph — polling deltas (2s interval)
  const [realtimeData, setRealtimeData] = useState<{ time: string; succeeded: number; failed: number }[]>([]);
  const prevTotals = useRef<{ succeeded: number; failed: number } | null>(null);

  useEffect(() => {
    if (!stats) return;
    const current = { succeeded: stats.totalSucceeded, failed: stats.totalFailed };

    if (prevTotals.current) {
      const delta = {
        time: new Date().toLocaleTimeString(),
        succeeded: current.succeeded - prevTotals.current.succeeded,
        failed: current.failed - prevTotals.current.failed,
      };
      setRealtimeData(prev => [...prev.slice(-299), delta]); // keep last 300 points (5 min at 1s)
    }
    prevTotals.current = current;
  }, [stats]);

  // Historical graph — hourly data
  const [history, setHistory] = useState<StatsHistoryPoint[]>([]);

  useEffect(() => {
    getStatsHistory(24).then(setHistory).catch(() => {});
    const id = setInterval(() => {
      getStatsHistory(24).then(setHistory).catch(() => {});
    }, 60000); // refresh every minute
    return () => clearInterval(id);
  }, []);

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

      {/* Realtime Graph — last 5 minutes */}
      <Card className="mb-8">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Realtime (jobs/sec)</CardTitle></CardHeader>
        <CardContent>
          {realtimeData.length > 1 ? (
            <ResponsiveContainer width="100%" height={200}>
              <AreaChart data={realtimeData}>
                <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
                <XAxis dataKey="time" tick={{ fontSize: 10 }} className="text-muted-foreground" />
                <YAxis tick={{ fontSize: 10 }} allowDecimals={false} className="text-muted-foreground" />
                <Tooltip />
                <Area type="monotone" dataKey="succeeded" stackId="1" stroke="#22c55e" fill="#22c55e" fillOpacity={0.3} isAnimationActive={false} />
                <Area type="monotone" dataKey="failed" stackId="1" stroke="#ef4444" fill="#ef4444" fillOpacity={0.3} isAnimationActive={false} />
              </AreaChart>
            </ResponsiveContainer>
          ) : (
            <div className="h-[200px] flex items-center justify-center text-sm text-muted-foreground">
              Collecting data...
            </div>
          )}
        </CardContent>
      </Card>

      {/* Historical Graph — last 24 hours */}
      {history.length > 0 && (
        <Card className="mb-8">
          <CardHeader className="pb-2"><CardTitle className="text-sm">Last 24 Hours</CardTitle></CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={200}>
              <AreaChart data={history.map(h => ({ ...h, hour: new Date(h.hour).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) }))}>
                <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
                <XAxis dataKey="hour" tick={{ fontSize: 10 }} className="text-muted-foreground" />
                <YAxis tick={{ fontSize: 10 }} allowDecimals={false} className="text-muted-foreground" />
                <Tooltip />
                <Area type="monotone" dataKey="succeeded" stackId="1" stroke="#22c55e" fill="#22c55e" fillOpacity={0.3} />
                <Area type="monotone" dataKey="failed" stackId="1" stroke="#ef4444" fill="#ef4444" fillOpacity={0.3} />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      )}

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
