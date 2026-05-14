import { Link, useLocation, useNavigate } from 'react-router-dom';
import type { DashboardStatistics } from '@/types';

export type EntityKind = 'jobs' | 'batches' | 'messages';

interface SidebarItem {
  to: string;
  label: string;
  count: number;
  // Token classes from the design system — match stateColor() in utils/format.ts.
  color: string;
}

// State-token classes mirror utils/format.ts stateColor() so that nav badges
// and inline state badges share the same semantic palette.
const tokens = {
  enqueued: 'bg-state-enqueued-bg text-state-enqueued',
  scheduled: 'bg-state-scheduled-bg text-state-scheduled',
  processing: 'bg-state-processing-bg text-state-processing',
  completed: 'bg-state-completed-bg text-state-completed',
  failed: 'bg-state-failed-bg text-state-failed',
  awaiting: 'bg-state-awaiting-bg text-state-awaiting',
  deleted: 'bg-state-deleted-bg text-state-deleted',
};

function itemsFor(entity: EntityKind, stats: DashboardStatistics | null): SidebarItem[] {
  if (entity === 'jobs') {
    return [
      { to: '/jobs/enqueued', label: 'Enqueued', count: stats?.created ?? 0, color: tokens.enqueued },
      { to: '/jobs/scheduled', label: 'Scheduled', count: stats?.scheduled ?? 0, color: tokens.scheduled },
      { to: '/jobs/processing', label: 'Processing', count: stats?.processing ?? 0, color: tokens.processing },
      { to: '/jobs/completed', label: 'Completed', count: stats?.completed ?? 0, color: tokens.completed },
      { to: '/jobs/failed', label: 'Failed', count: stats?.failed ?? 0, color: tokens.failed },
      { to: '/jobs/awaiting', label: 'Awaiting', count: stats?.awaiting ?? 0, color: tokens.awaiting },
      { to: '/jobs/deleted', label: 'Deleted', count: stats?.deleted ?? 0, color: tokens.deleted },
    ];
  }

  if (entity === 'batches') {
    return [
      { to: '/batches/processing', label: 'Processing', count: stats?.batchesProcessing ?? 0, color: tokens.processing },
      { to: '/batches/awaiting', label: 'Awaiting', count: stats?.batchesAwaiting ?? 0, color: tokens.awaiting },
      { to: '/batches/completed', label: 'Completed', count: stats?.batchesCompleted ?? 0, color: tokens.completed },
      { to: '/batches/failed', label: 'Failed', count: stats?.batchesFailed ?? 0, color: tokens.failed },
      { to: '/batches/deleted', label: 'Deleted', count: stats?.batchesDeleted ?? 0, color: tokens.deleted },
    ];
  }

  return [
    { to: '/messages/enqueued', label: 'Enqueued', count: stats?.messagesEnqueued ?? 0, color: tokens.enqueued },
    { to: '/messages/processing', label: 'Processing', count: stats?.messagesProcessing ?? 0, color: tokens.processing },
    { to: '/messages/completed', label: 'Completed', count: stats?.messagesCompleted ?? 0, color: tokens.completed },
    { to: '/messages/failed', label: 'Failed', count: stats?.messagesFailed ?? 0, color: tokens.failed },
  ];
}

const titles: Record<EntityKind, string> = {
  jobs: 'Jobs',
  batches: 'Batches',
  messages: 'Messages',
};

interface Props {
  entity: EntityKind;
  stats: DashboardStatistics | null;
  // Optional callback invoked when a nav item is clicked — used by the mobile
  // drawer to close itself after the user navigates.
  onNavigate?: () => void;
}

export default function EntityStateSidebar({ entity, stats, onNavigate }: Props) {
  const location = useLocation();
  const navigate = useNavigate();

  const items = itemsFor(entity, stats);

  return (
    <nav aria-label={`${titles[entity]} states`} className="space-y-1">
      <h3 className="text-xs font-semibold text-muted-foreground uppercase mb-3 px-3">
        {titles[entity]}
      </h3>
      {items.map((item) => {
        const isActive = location.pathname === item.to;

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
            className={`flex items-center justify-between px-3 py-2 rounded-md text-sm transition-colors ${
              isActive
                ? 'bg-accent text-accent-foreground font-medium'
                : 'text-muted-foreground hover:bg-accent/50'
            }`}
          >
            <span>{item.label}</span>
            <span
              className={`text-xs px-2 py-0.5 rounded-full font-medium tabular-nums ${
                item.count > 0 ? item.color : 'text-muted-foreground/50'
              }`}
            >
              {item.count}
            </span>
          </Link>
        );
      })}
    </nav>
  );
}
