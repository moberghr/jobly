import { Link, useLocation, useNavigate } from 'react-router-dom';
import type { DashboardStatistics } from '@/types';
import type { NavItem } from '@/layouts/navItems';

interface NavBadgeProps {
  count: number;
  variant: 'info' | 'danger' | 'muted';
}

function NavBadge({ count, variant }: NavBadgeProps) {
  const cls =
    variant === 'danger'
      ? 'bg-state-failed-bg text-state-failed font-bold'
      : variant === 'info'
      ? 'bg-state-enqueued-bg text-state-enqueued'
      : 'bg-muted text-muted-foreground';

  return (
    <span
      className={`ml-1 text-xs min-w-10 text-center tabular-nums px-1.5 py-0.5 rounded-full ${cls}`}
    >
      {count}
    </span>
  );
}

function renderBadges(label: string, stats: DashboardStatistics | null, showBadges: boolean) {
  if (!stats || !showBadges) {
    return null;
  }

  if (label === 'Jobs') {
    return (
      <>
        {stats.created > 0 && <NavBadge count={stats.created} variant="info" />}
        {stats.failed > 0 && <NavBadge count={stats.failed} variant="danger" />}
      </>
    );
  }
  if (label === 'Messages') {
    return (
      <>
        {stats.messages > 0 && <NavBadge count={stats.messages} variant="info" />}
        {stats.messagesFailed > 0 && <NavBadge count={stats.messagesFailed} variant="danger" />}
      </>
    );
  }
  if (label === 'Batches') {
    return (
      <>
        {stats.batchesProcessing > 0 && <NavBadge count={stats.batchesProcessing} variant="info" />}
        {stats.batchesFailed > 0 && <NavBadge count={stats.batchesFailed} variant="danger" />}
      </>
    );
  }
  if (label === 'Servers') {
    return <NavBadge count={stats.servers} variant="muted" />;
  }

  return null;
}

interface Props {
  items: NavItem[];
  stats: DashboardStatistics | null;
  // When false (e.g. on mobile drawer), badges are hidden to avoid overflow.
  showBadges?: boolean;
  // Layout direction — horizontal for navbar, vertical for mobile drawer.
  orientation?: 'horizontal' | 'vertical';
  onNavigate?: () => void;
}

export default function TopLevelNav({
  items,
  stats,
  showBadges = true,
  orientation = 'horizontal',
  onNavigate,
}: Props) {
  const location = useLocation();
  const navigate = useNavigate();

  const layoutCls = orientation === 'horizontal' ? 'flex gap-1' : 'flex flex-col gap-1';

  return (
    <nav className={layoutCls} aria-label="Primary">
      {items.map((item) => {
        const Icon = item.icon;
        const matchPath = item.to.includes('/') && item.to !== '/' ? '/' + item.to.split('/')[1] : item.to;
        const isActive = item.to === '/' ? location.pathname === '/' : location.pathname.startsWith(matchPath);

        return (
          <Link
            key={item.to}
            to={item.to}
            onClick={(e) => {
              if (isActive) {
                e.preventDefault();
                navigate(item.to, { replace: true, state: { refreshKey: Date.now() } });
              }
              onNavigate?.();
            }}
            className={`flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
              isActive
                ? 'bg-primary text-primary-foreground'
                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
            }`}
          >
            <Icon className="h-4 w-4" />
            <span>{item.label}</span>
            {renderBadges(item.label, stats, showBadges)}
          </Link>
        );
      })}
    </nav>
  );
}
