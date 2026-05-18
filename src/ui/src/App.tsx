import { useState, useEffect, useCallback } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import MainLayout from '@/layouts/MainLayout';
import DashboardPage from '@/pages/dashboard/DashboardPage';
import JobListPage from '@/pages/jobs/JobListPage';
import MessagesPage from '@/pages/messages/MessagesPage';
import BatchesPage from '@/pages/batches/BatchesPage';
import RecurringPage from '@/pages/recurring/RecurringPage';
import RecurringDetailPage from '@/pages/recurring/RecurringDetailPage';
import ServersPage from '@/pages/servers/ServersPage';
import ServerDetailPage from '@/pages/servers/ServerDetailPage';
import CountersPage from '@/pages/counters/CountersPage';
import ConcurrencyLimitsPage from '@/pages/concurrency/ConcurrencyLimitsPage';
import RateLimitsPage from '@/pages/ratelimits/RateLimitsPage';
import SagasListPage from '@/pages/sagas/SagasListPage';
import SagaDetailPage from '@/pages/sagas/SagaDetailPage';
import BackgroundServicesList from '@/pages/BackgroundServices/List';
import BackgroundServiceDetail from '@/pages/BackgroundServices/Detail';
import WorkerDetailPage from '@/pages/workers/WorkerDetailPage';
import TracePage from '@/pages/trace/TracePage';
import DetailPage from '@/pages/detail/DetailPage';
import LoginPage from '@/pages/auth/LoginPage';
import ExtensionPage from '@/extensions/ExtensionPage';
import { setOnUnauthorized } from '@/api/client';
import { loadExtensions } from '@/extensions/loader';
import { extensionRuntime } from '@/extensions/runtime';
import { getAuthStatus } from '@/api';
import { config } from '@/config';
import type { ExtensionManifest } from '@/extensions/types';

function App() {
  const [needsLogin, setNeedsLogin] = useState(false);
  const [extensions, setExtensions] = useState<ExtensionManifest[]>([]);
  const [extensionsLoaded, setExtensionsLoaded] = useState(false);
  // Cold-boot gate so we don't fire any other API calls before we know whether
  // the user is authenticated. Skipped entirely when the built-in login addon
  // isn't enabled — those deployments have no 401 problem.
  const [authProbeDone, setAuthProbeDone] = useState(!config.hasBuiltInLogin);

  const initExtensions = useCallback(() => {
    loadExtensions().then((manifests) => {
      setExtensions(manifests);
      setExtensionsLoaded(true);
    });
  }, []);

  useEffect(() => {
    if (config.hasBuiltInLogin) {
      // Keep the 401 interceptor as the fallback for session-expired scenarios
      // mid-session; the cold-boot path no longer relies on it.
      setOnUnauthorized(() => setNeedsLogin(true));

      getAuthStatus()
        .then((s) => {
          if (s.authenticated) {
            initExtensions();
          } else {
            setNeedsLogin(true);
          }
        })
        .catch(() => {
          // Probe failed (network, server down). Treat as unauthenticated so the
          // login page renders; the user can retry from there.
          setNeedsLogin(true);
        })
        .finally(() => setAuthProbeDone(true));
    } else {
      initExtensions();
    }

    return () => extensionRuntime.stop();
  }, [initExtensions]);

  const handleLogin = useCallback(() => {
    setNeedsLogin(false);
    // Now authenticated — load extensions. MainLayout's mount-effect re-runs getAddons()
    // and drives both nav-visibility and connectIfEnabled, so we don't duplicate the
    // request here.
    initExtensions();
  }, [initExtensions]);

  if (!authProbeDone) {
    return null;
  }

  if (needsLogin) {
    return <LoginPage onLogin={handleLogin} />;
  }

  // Wait for extensions to load before rendering routes so dynamic pages are available
  if (!extensionsLoaded) {
    return null;
  }

  const extensionPages = extensionRuntime.getPages();

  return (
    <BrowserRouter basename={config.basePath}>
      <Routes>
        <Route element={<MainLayout extensions={extensions} />}>
          <Route index element={<DashboardPage />} />
          <Route path="/detail/:id" element={<DetailPage />} />
          <Route path="/jobs/detail/:id" element={<DetailPage />} />
          <Route path="/jobs/:state" element={<JobListPage />} />
          <Route path="/messages/detail/:id" element={<DetailPage />} />
          <Route path="/messages/:state" element={<MessagesPage />} />
          <Route path="/batches/detail/:id" element={<DetailPage />} />
          <Route path="/batches/:state" element={<BatchesPage />} />
          <Route path="/recurring/:id" element={<RecurringDetailPage />} />
          <Route path="/recurring" element={<RecurringPage />} />
          <Route path="/trace/:traceId/:highlightId?" element={<TracePage />} />
          <Route path="/workers/:id" element={<WorkerDetailPage />} />
          <Route path="/servers/:id" element={<ServerDetailPage />} />
          <Route path="/servers" element={<ServersPage />} />
          <Route path="/counters" element={<CountersPage />} />
          <Route path="/concurrency" element={<ConcurrencyLimitsPage />} />
          <Route path="/ratelimits" element={<RateLimitsPage />} />
          <Route path="/sagas/:id" element={<SagaDetailPage />} />
          <Route path="/sagas" element={<SagasListPage />} />
          <Route path="/services/:name" element={<BackgroundServiceDetail />} />
          <Route path="/services" element={<BackgroundServicesList />} />

          {/* Extension pages */}
          {extensionPages.map((page) => (
            <Route
              key={page.path}
              path={page.path}
              element={<ExtensionPage component={page.component} />}
            />
          ))}
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
