import { useEffect, useState } from 'react';
import axios from 'axios';
import { Outlet, useLocation } from 'react-router-dom';
import { useDashboardStore } from '@/stores/dashboard';
import { useRealtimeStore } from '@/stores/realtime';
import { usePageStore } from '@/stores/page';
import * as api from '@/api';
import type { ExtensionManifest } from '@/extensions/types';
import WarpSidebar from '@/layouts/WarpSidebar';
import WarpTopbar from '@/layouts/WarpTopbar';
import MobileDrawer from '@/layouts/MobileDrawer';
import { buildWarpNavItems } from '@/layouts/warpNavItems';
import { useRealtimeInvalidation } from '@/hooks/useRealtimeInvalidation';

export default function MainLayout({ extensions = [] }: { extensions?: ExtensionManifest[] }) {
  const { error, fetchStats } = useDashboardStore();
  const location = useLocation();
  const [concurrencyAvailable, setConcurrencyAvailable] = useState(false);
  const [rateLimitsAvailable, setRateLimitsAvailable] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [sagasAvailable, setSagasAvailable] = useState(false);

  const title = usePageStore((s) => s.title);
  const subtitle = usePageStore((s) => s.subtitle);
  const right = usePageStore((s) => s.right);

  useRealtimeInvalidation();

  // Initial fetch for first paint. Further updates arrive via SignalR push.
  useEffect(() => {
    fetchStats();
  }, [fetchStats]);

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

    api
      .getSagaStats()
      .then(() => {
        if (!cancelled) setSagasAvailable(true);
      })
      .catch(() => {
        if (!cancelled) setSagasAvailable(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // Close drawer on route change.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname]);

  const navItems = buildWarpNavItems(
    extensions,
    concurrencyAvailable,
    rateLimitsAvailable,
    sagasAvailable,
  );

  return (
    <div className="relative min-h-screen flex bg-background text-foreground">
      <div className="warp-ambient" aria-hidden />

      <WarpSidebar items={navItems} />

      <div className="flex-1 flex flex-col min-w-0 relative">
        <WarpTopbar
          title={title}
          subtitle={subtitle}
          right={right}
          onMenuClick={() => setDrawerOpen(true)}
        />

        {error && (
          <div
            role="alert"
            className="mx-4 lg:mx-6 mt-3 rounded-md bg-warp-red-soft ring-1 ring-warp-red/30 px-3 py-2 text-sm text-warp-red flex items-center gap-2"
          >
            <span className="font-medium">Connection lost</span>
            <span className="opacity-80">— Unable to connect to Warp API. Retrying...</span>
          </div>
        )}

        <main className="flex-1 p-4 lg:p-6 min-w-0 overflow-auto">
          <Outlet />
        </main>
      </div>

      <MobileDrawer open={drawerOpen} onOpenChange={setDrawerOpen}>
        <WarpSidebar items={navItems} mobile onNavigate={() => setDrawerOpen(false)} />
      </MobileDrawer>
    </div>
  );
}
