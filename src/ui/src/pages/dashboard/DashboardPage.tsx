import { useState, useEffect } from 'react';
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { useDashboardStore } from '@/stores/dashboard';
import { MetricCard } from '@/components/MetricCard';
import { RealtimeChart } from '@/components/RealtimeChart';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { getStatsHistory } from '@/api';
import type { StatsHistoryPoint } from '@/types';
import {
  Briefcase,
  XCircle,
  Clock,
  Server,
  Mail,
  Loader,
  Hourglass,
  AlertTriangle,
} from 'lucide-react';

function padHistory(data: StatsHistoryPoint[], hours: number) {
  const now = new Date();
  now.setMinutes(0, 0, 0);
  const result = [];
  const dataMap = new Map(data.map(d => [new Date(d.hour).getTime(), d]));

  for (var i = hours - 1; i >= 0; i--) {
    const hourDate = new Date(now.getTime() - i * 3600000);
    const key = hourDate.getTime();
    const point = dataMap.get(key);
    const label = hours <= 24
      ? hourDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
      : hourDate.toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' });

    result.push({
      label,
      succeeded: point?.succeeded ?? 0,
      failed: point?.failed ?? 0,
    });
  }

  return result;
}

export default function DashboardPage() {
  const { stats, error } = useDashboardStore();

  // Historical graph — hourly data
  const [history, setHistory] = useState<StatsHistoryPoint[]>([]);
  const [historyHours, setHistoryHours] = useState(24);

  useEffect(() => {
    getStatsHistory(historyHours).then(setHistory).catch(() => {});
    const id = setInterval(() => {
      getStatsHistory(historyHours).then(setHistory).catch(() => {});
    }, 60000);
    return () => clearInterval(id);
  }, [historyHours]);

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
      <div className="grid grid-cols-7 gap-4 mb-8">
        <MetricCard label="Enqueued" value={stats.created} icon={<Briefcase className="h-5 w-5" />} />
        <MetricCard label="Processing" value={stats.processing} icon={<Loader className="h-5 w-5" />} color="text-purple-600" />
        <MetricCard label="Scheduled" value={stats.scheduled} icon={<Clock className="h-5 w-5" />} />
        <MetricCard label="Awaiting" value={stats.awaiting} icon={<Hourglass className="h-5 w-5" />} />
        <MetricCard label="Failed" value={stats.failed} icon={<XCircle className="h-5 w-5" />} color="text-red-600" />
        <MetricCard label="Messages" value={stats.messages} icon={<Mail className="h-5 w-5" />} />
        <MetricCard label="Servers" value={stats.servers} icon={<Server className="h-5 w-5" />} />
      </div>

      {/* Realtime Graph */}
      <Card className="mb-8">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Realtime — last 60 seconds</CardTitle></CardHeader>
        <CardContent>
          <RealtimeChart height={200} />
        </CardContent>
      </Card>

      {/* Historical Graph */}
      <Card>
        <CardHeader className="pb-2">
          <div className="flex items-center justify-between">
            <CardTitle className="text-sm">History</CardTitle>
            <div className="flex gap-1">
              {[
                { label: '24h', hours: 24 },
                { label: '7d', hours: 168 },
              ].map(({ label, hours }) => (
                <button
                  key={label}
                  onClick={() => { setHistoryHours(hours); getStatsHistory(hours).then(setHistory).catch(() => {}); }}
                  className={`px-2 py-0.5 text-xs rounded-md transition-colors ${
                    historyHours === hours
                      ? 'bg-primary text-primary-foreground'
                      : 'text-muted-foreground hover:bg-accent'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <ResponsiveContainer width="100%" height={200}>
            <AreaChart data={padHistory(history, historyHours)}>
              <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
              <XAxis dataKey="label" tick={{ fontSize: 10 }} className="text-muted-foreground" />
              <YAxis tick={{ fontSize: 10 }} allowDecimals={false} className="text-muted-foreground" />
              <Tooltip />
              <Area type="monotone" dataKey="succeeded" stackId="1" stroke="#22c55e" fill="#22c55e" fillOpacity={0.3} />
              <Area type="monotone" dataKey="failed" stackId="1" stroke="#ef4444" fill="#ef4444" fillOpacity={0.3} />
            </AreaChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>
    </div>
  );
}
