import { useEffect, useState } from 'react';
import axios from 'axios';
import { Outlet, useLocation } from 'react-router-dom';
import { useDashboardStore } from '@/stores/dashboard';
import { useRealtimeStore } from '@/stores/realtime';
import * as api from '@/api';
import type { ExtensionManifest } from '@/extensions/types';
import Navbar from '@/layouts/Navbar';
import TopLevelNav from '@/layouts/TopLevelNav';
import { buildNavItems } from '@/layouts/navItems';
import MobileDrawer from '@/layouts/MobileDrawer';
import EntityStateSidebar, { type EntityKind } from '@/components/EntityStateSidebar';

function detectEntity(pathname: string): EntityKind | null {
  if (pathname.startsWith('/jobs')) return 'jobs';
  if (pathname.startsWith('/batches')) return 'batches';
  if (pathname.startsWith('/messages')) return 'messages';

  return null;
}

export default function MainLayout({ extensions = [] }: { extensions?: ExtensionManifest[] }) {
  const { stats, error, fetchStats } = useDashboardStore();
  const location = useLocation();
  const [concurrencyAvailable, setConcurrencyAvailable] = useState(false);
  const [rateLimitsAvailable, setRateLimitsAvailable] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);

  // Initial fetch for first paint — after this, fresh stats arrive directly via
  // the SignalR push payload on every JobFinalized / MessageEnqueued event (see
  // bridgeEvent in stores/realtime.ts) and are written straight into the dashboard
  // store. No event-driven REST refetch is needed for stats; the bus emit fired
  // by the bridge still wakes other pages (jobs, counters, etc.) to refetch their
  // own scoped views.
  useEffect(() => {
    fetchStats();
  }, [fetchStats]);

  // Kick off the realtime probe + hub connection once the dashboard has
  // mounted (and therefore subscribers like the bus listeners are already
  // registered). Running this from main.tsx races React's useEffect timing
  // and the post-connect drain emits to zero subscribers.
  useEffect(() => {
    void useRealtimeStore.getState().probeAndConnect();
  }, []);

  useEffect(() => {
    let cancelled = false;
    api
      .listConcurrencyLimits()
      .then(() => {
        if (!cancelled) setConcurrencyAvailable(true);
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        if (axios.isAxiosError(e) && e.response?.status === 404) {
          setConcurrencyAvailable(false);
        } else {
          // Non-404 errors (network, 500): keep hidden, don't surface noise.
          setConcurrencyAvailable(false);
        }
      });

    api
      .listRateLimits()
      .then(() => {
        if (!cancelled) setRateLimitsAvailable(true);
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        if (axios.isAxiosError(e) && e.response?.status === 404) {
          setRateLimitsAvailable(false);
        } else {
          setRateLimitsAvailable(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // Close drawer on route change.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname]);

  const navItems = buildNavItems(extensions, concurrencyAvailable, rateLimitsAvailable);
  const entity = detectEntity(location.pathname);

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <Navbar
        desktopNav={<TopLevelNav items={navItems} stats={stats} />}
        onMenuClick={() => setDrawerOpen(true)}
      />

      {error && (
        <div
          role="alert"
          className="bg-destructive/10 border-b border-destructive/20 px-4 lg:px-6 py-2 text-sm text-destructive flex items-center gap-2"
        >
          <span className="font-medium">Connection lost</span>
          <span className="text-destructive/70">— Unable to connect to Warp API. Retrying...</span>
        </div>
      )}

      <div className="flex flex-1">
        {entity && (
          <aside className="hidden lg:block w-64 shrink-0 border-r bg-card min-h-[calc(100vh-3.5rem)] p-4">
            <EntityStateSidebar entity={entity} stats={stats} />
          </aside>
        )}

        <main className="flex-1 p-4 lg:p-6 min-w-0">
          <Outlet />
        </main>
      </div>

      <MobileDrawer open={drawerOpen} onOpenChange={setDrawerOpen}>
        <TopLevelNav
          items={navItems}
          stats={stats}
          orientation="vertical"
          showBadges={false}
          onNavigate={() => setDrawerOpen(false)}
        />
        {entity && (
          <div>
            <EntityStateSidebar
              entity={entity}
              stats={stats}
              onNavigate={() => setDrawerOpen(false)}
            />
          </div>
        )}
      </MobileDrawer>

      <footer className="border-t bg-card px-4 lg:px-6 py-3 text-xs text-muted-foreground flex items-center justify-between gap-4">
        <span className="truncate">{stats?.databaseConnection ?? 'Warp Dashboard'}</span>
        <div className="flex items-center gap-4 tabular-nums shrink-0">
          {stats && <span className="hidden sm:inline">Servers: {stats.servers} · Workers active</span>}
          <span>UTC: {new Date().toISOString().replace('T', ' ').substring(0, 19)}</span>
        </div>
      </footer>
    </div>
  );
}
