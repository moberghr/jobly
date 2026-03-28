import { Link, Outlet, useLocation } from 'react-router-dom';
import { useDashboardStore } from '@/stores/dashboard';
import { usePolling } from '@/hooks/usePolling';
import {
  LayoutDashboard,
  Briefcase,
  Mail,
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
  { to: '/recurring', label: 'Recurring', icon: RefreshCw },
  { to: '/servers', label: 'Servers', icon: Server },
];

export default function MainLayout() {
  const { stats, fetchStats } = useDashboardStore();
  const location = useLocation();
  const { theme, toggle } = useTheme();

  usePolling(fetchStats, 2000);

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
              const isActive = item.to === '/'
                ? location.pathname === '/'
                : location.pathname.startsWith(item.to);
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
                  {item.label === 'Jobs' && stats && (
                    <span className="ml-1 text-xs opacity-75">
                      {stats.created + stats.failed}
                    </span>
                  )}
                  {item.label === 'Messages' && stats && (
                    <span className="ml-1 text-xs opacity-75">{stats.messages}</span>
                  )}
                  {item.label === 'Servers' && stats && (
                    <span className="ml-1 text-xs opacity-75">{stats.servers}</span>
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
    </div>
  );
}

function JobsSidebar({ stats }: { stats: DashboardStatistics | null }) {
  const location = useLocation();

  const sidebarItems = [
    { to: '/jobs/enqueued', label: 'Enqueued', count: stats?.created ?? 0 },
    { to: '/jobs/scheduled', label: 'Scheduled', count: stats?.scheduled ?? 0 },
    { to: '/jobs/processing', label: 'Processing', count: stats?.processing ?? 0 },
    { to: '/jobs/completed', label: 'Completed', count: stats?.completed ?? 0 },
    { to: '/jobs/failed', label: 'Failed', count: stats?.failed ?? 0 },
    { to: '/jobs/awaiting', label: 'Awaiting', count: stats?.awaiting ?? 0 },
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
              <span className={`text-xs px-2 py-0.5 rounded-full ${
                item.count > 0 ? 'bg-muted text-muted-foreground' : 'text-muted-foreground/50'
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
