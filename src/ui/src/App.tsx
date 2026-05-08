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
import WorkerDetailPage from '@/pages/workers/WorkerDetailPage';
import TracePage from '@/pages/trace/TracePage';
import DetailPage from '@/pages/detail/DetailPage';
import LoginPage from '@/pages/auth/LoginPage';
import ExtensionPage from '@/extensions/ExtensionPage';
import { setOnUnauthorized } from '@/api/client';
import { loadExtensions } from '@/extensions/loader';
import { extensionRuntime } from '@/extensions/runtime';
import { config } from '@/config';
import type { ExtensionManifest } from '@/extensions/types';

function App() {
  const [needsLogin, setNeedsLogin] = useState(false);
  const [extensions, setExtensions] = useState<ExtensionManifest[]>([]);
  const [extensionsLoaded, setExtensionsLoaded] = useState(false);

  const initExtensions = useCallback(() => {
    loadExtensions().then((manifests) => {
      setExtensions(manifests);
      setExtensionsLoaded(true);
    });
  }, []);

  useEffect(() => {
    if (config.hasBuiltInLogin) {
      setOnUnauthorized(() => setNeedsLogin(true));
    }

    // Load extensions immediately — if auth is required, the API call will
    // get a 401 and the loader will return an empty array gracefully.
    // After login, we reload extensions.
    initExtensions();

    return () => extensionRuntime.stop();
  }, [initExtensions]);

  const handleLogin = useCallback(() => {
    setNeedsLogin(false);
    // Now authenticated — load extensions
    initExtensions();
  }, [initExtensions]);

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
