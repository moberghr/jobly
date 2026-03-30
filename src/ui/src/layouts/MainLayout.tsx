import { Link, Outlet, useLocation } from 'react-router-dom';
import { useDashboardStore } from '@/stores/dashboard';
import { usePolling } from '@/hooks/usePolling';
import {
  LayoutDashboard,
  Briefcase,
  Mail,
  Layers,
  RefreshCw,
  Server,
  Moon,
  Sun,
} from 'lucide-react';
import { useTheme } from '@/hooks/useTheme';
import type { DashboardStatistics } from '@/types';

const navItems = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/jobs/enqueued', label: 'Jobs', icon: Briefcase },
  { to: '/messages', label: 'Messages', icon: Mail },
  { to: '/batches', label: 'Batches', icon: Layers },
  { to: '/recurring', label: 'Recurring', icon: RefreshCw },
  { to: '/servers', label: 'Servers', icon: Server },
];

export default function MainLayout() {
  const { stats, fetchStats } = useDashboardStore();
  const location = useLocation();
  const { theme, toggle } = useTheme();

  usePolling(fetchStats, 1000);

  const isJobsSection = location.pathname.startsWith('/jobs');

  return (
    <div className="min-h-screen bg-background">
      {/* Top navbar */}
      <header className="border-b bg-card">
        <div className="flex h-14 items-center px-6">
          <Link to="/" className="text-lg font-bold mr-8">Jobly</Link>
          <nav className="flex gap-1">
            {navItems.map((item) => {
              const Icon = item.icon;
              const matchPath = item.to === '/jobs/enqueued' ? '/jobs' : item.to;
              const isActive = item.to === '/'
                ? location.pathname === '/'
                : location.pathname.startsWith(matchPath);
              return (
                <Link
                  key={item.to}
                  to={item.to}
                  className={`flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
                    isActive
                      ? 'bg-primary text-primary-foreground'
                      : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                  }`}
                >
                  <Icon className="h-4 w-4" />
                  {item.label}
                  {item.label === 'Jobs' && stats && stats.created > 0 && (
                    <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300">
                      {stats.created}
                    </span>
                  )}
                  {item.label === 'Jobs' && stats && stats.failed > 0 && (
                    <span className="text-xs px-1.5 py-0.5 rounded-full bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300 font-bold">
                      {stats.failed}
                    </span>
                  )}
                  {item.label === 'Messages' && stats && stats.messages > 0 && (
                    <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300">
                      {stats.messages}
                    </span>
                  )}
                  {item.label === 'Batches' && stats && stats.batches > 0 && (
                    <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300">
                      {stats.batches}
                    </span>
                  )}
                  {item.label === 'Servers' && stats && (
                    <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground">{stats.servers}</span>
                  )}
                </Link>
              );
            })}
          </nav>
          <div className="flex-1" />
          <button onClick={toggle} className="p-2 rounded-md hover:bg-accent text-muted-foreground">
            {theme === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </button>
        </div>
      </header>

      <div className="flex">
        {/* Left sidebar for jobs section */}
        {isJobsSection && <JobsSidebar stats={stats} />}

        {/* Main content */}
        <main className={`flex-1 p-6 ${isJobsSection ? '' : ''}`}>
          <Outlet />
        </main>
      </div>

      {/* Footer */}
      <footer className="border-t bg-card px-6 py-3 text-xs text-muted-foreground flex items-center justify-between">
        <span>Jobly Dashboard</span>
        <div className="flex items-center gap-4">
          {stats && <span>Servers: {stats.servers} · Workers active</span>}
          <span>UTC: {new Date().toISOString().replace('T', ' ').substring(0, 19)}</span>
        </div>
      </footer>
    </div>
  );
}

function JobsSidebar({ stats }: { stats: DashboardStatistics | null }) {
  const location = useLocation();

  const sidebarItems = [
    { to: '/jobs/enqueued', label: 'Enqueued', count: stats?.created ?? 0, color: 'bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300' },
    { to: '/jobs/scheduled', label: 'Scheduled', count: stats?.scheduled ?? 0, color: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900 dark:text-yellow-300' },
    { to: '/jobs/processing', label: 'Processing', count: stats?.processing ?? 0, color: 'bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300' },
    { to: '/jobs/completed', label: 'Completed', count: stats?.completed ?? 0, color: 'bg-green-100 text-green-700 dark:bg-green-900 dark:text-green-300' },
    { to: '/jobs/failed', label: 'Failed', count: stats?.failed ?? 0, color: 'bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300' },
    { to: '/jobs/awaiting', label: 'Awaiting', count: stats?.awaiting ?? 0, color: 'bg-orange-100 text-orange-700 dark:bg-orange-900 dark:text-orange-300' },
  ];

  return (
    <aside className="w-56 border-r bg-card min-h-[calc(100vh-3.5rem)] p-4">
      <h3 className="text-xs font-semibold text-muted-foreground uppercase mb-3">Jobs</h3>
      <nav className="space-y-1">
        {sidebarItems.map((item) => {
          const isActive = location.pathname === item.to;
          return (
            <Link
              key={item.to}
              to={item.to}
              className={`flex items-center justify-between px-3 py-2 rounded-md text-sm transition-colors ${
                isActive
                  ? 'bg-accent text-accent-foreground font-medium'
                  : 'text-muted-foreground hover:bg-accent/50'
              }`}
            >
              <span>{item.label}</span>
              <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                item.count > 0 ? item.color : 'text-muted-foreground/50'
              }`}>
                {item.count}
              </span>
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
