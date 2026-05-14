import {
  LayoutDashboard,
  Briefcase,
  Mail,
  Layers,
  RefreshCw,
  Server,
  Gauge,
  KeyRound,
  Timer,
  Puzzle,
} from 'lucide-react';
import * as LucideIcons from 'lucide-react';
import type { ExtensionManifest } from '@/extensions/types';

export interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
}

const builtInNavItems: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/jobs/enqueued', label: 'Jobs', icon: Briefcase },
  { to: '/messages/enqueued', label: 'Messages', icon: Mail },
  { to: '/batches/processing', label: 'Batches', icon: Layers },
  { to: '/recurring', label: 'Recurring', icon: RefreshCw },
  { to: '/servers', label: 'Servers', icon: Server },
  { to: '/counters', label: 'Counters', icon: Gauge },
];

const concurrencyNavItem: NavItem = { to: '/concurrency', label: 'Concurrency', icon: KeyRound };
const rateLimitsNavItem: NavItem = { to: '/ratelimits', label: 'Rate Limits', icon: Timer };

function resolveIcon(name?: string): React.ComponentType<{ className?: string }> {
  if (!name) {
    return Puzzle;
  }

  // Convert kebab-case to PascalCase (e.g., "refresh-cw" → "RefreshCw")
  const pascalCase = name
    .split('-')
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join('');
  const icons = LucideIcons as Record<string, unknown>;

  return (icons[pascalCase] as React.ComponentType<{ className?: string }>) ?? Puzzle;
}

export function buildNavItems(
  extensions: ExtensionManifest[],
  concurrencyAvailable: boolean,
  rateLimitsAvailable: boolean,
): NavItem[] {
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
