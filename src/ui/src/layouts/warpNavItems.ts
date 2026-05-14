import {
  LayoutGrid,
  Briefcase,
  Mail,
  Layers,
  Repeat,
  Server,
  Zap,
  Clock,
  Loader,
  KeyRound,
  Timer,
  Puzzle,
} from 'lucide-react';
import * as LucideIcons from 'lucide-react';
import type { ExtensionManifest } from '@/extensions/types';
import type { DashboardStatistics } from '@/types';

export type BadgeKind = 'blue' | 'red';

export interface NavBadge {
  value: number;
  kind: BadgeKind;
}

export interface WarpNavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  /** Pull badge values from live stats. */
  badges?: (stats: DashboardStatistics | null) => NavBadge[];
}

function nonZero(n: number | undefined | null): number {
  return typeof n === 'number' && n > 0 ? n : 0;
}

const builtInNavItems: WarpNavItem[] = [
  {
    to: '/',
    label: 'Dashboard',
    icon: LayoutGrid,
  },
  {
    to: '/jobs/enqueued',
    label: 'Jobs',
    icon: Briefcase,
    badges: (s) => {
      const blue = nonZero(s?.pending);
      const red = nonZero(s?.failed);
      const out: NavBadge[] = [];
      if (blue) out.push({ value: blue, kind: 'blue' });
      if (red) out.push({ value: red, kind: 'red' });

      return out;
    },
  },
  {
    to: '/messages/enqueued',
    label: 'Messages',
    icon: Mail,
    badges: (s) => {
      const blue = nonZero(s?.messages);
      const red = nonZero(s?.messagesFailed);
      const out: NavBadge[] = [];
      if (blue) out.push({ value: blue, kind: 'blue' });
      if (red) out.push({ value: red, kind: 'red' });

      return out;
    },
  },
  {
    to: '/batches/processing',
    label: 'Batches',
    icon: Layers,
    badges: (s) => {
      const blue = nonZero(s?.batches);
      const red = nonZero(s?.batchesFailed);
      const out: NavBadge[] = [];
      if (blue) out.push({ value: blue, kind: 'blue' });
      if (red) out.push({ value: red, kind: 'red' });

      return out;
    },
  },
  {
    to: '/recurring',
    label: 'Recurring',
    icon: Repeat,
  },
  {
    to: '/servers',
    label: 'Servers',
    icon: Server,
    badges: (s) => {
      const blue = nonZero(s?.servers);

      return blue ? [{ value: blue, kind: 'blue' }] : [];
    },
  },
  // 'Workers' has no dedicated list page yet — link to servers detail surface.
  // Kept as a separate nav entry to match the V2 sidebar design.
  {
    to: '/servers',
    label: 'Workers',
    icon: Zap,
  },
  {
    to: '/trace',
    label: 'Trace',
    icon: Clock,
  },
  {
    to: '/counters',
    label: 'Counters',
    icon: Loader,
  },
];

const concurrencyNavItem: WarpNavItem = {
  to: '/concurrency',
  label: 'Concurrency',
  icon: KeyRound,
};
const rateLimitsNavItem: WarpNavItem = {
  to: '/ratelimits',
  label: 'Rate Limits',
  icon: Timer,
};

function resolveIcon(name?: string): React.ComponentType<{ className?: string }> {
  if (!name) {
    return Puzzle;
  }

  const pascalCase = name
    .split('-')
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join('');
  const icons = LucideIcons as Record<string, unknown>;

  return (icons[pascalCase] as React.ComponentType<{ className?: string }>) ?? Puzzle;
}

export function buildWarpNavItems(
  extensions: ExtensionManifest[],
  concurrencyAvailable: boolean,
  rateLimitsAvailable: boolean,
): WarpNavItem[] {
  return [
    ...builtInNavItems,
    ...(concurrencyAvailable ? [concurrencyNavItem] : []),
    ...(rateLimitsAvailable ? [rateLimitsNavItem] : []),
    ...extensions.flatMap((ext) =>
      ext.pages.map((page) => ({
        to: page.path,
        label: page.label,
        icon: resolveIcon(page.icon),
      })),
    ),
  ];
}
