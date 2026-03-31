import { BrowserRouter, Routes, Route } from 'react-router-dom';
import MainLayout from '@/layouts/MainLayout';
import DashboardPage from '@/pages/dashboard/DashboardPage';
import JobListPage from '@/pages/jobs/JobListPage';
import JobDetailPage from '@/pages/jobs/JobDetailPage';
import MessagesPage from '@/pages/messages/MessagesPage';
import MessageDetailPage from '@/pages/messages/MessageDetailPage';
import BatchesPage from '@/pages/batches/BatchesPage';
import BatchDetailPage from '@/pages/batches/BatchDetailPage';
import RecurringPage from '@/pages/recurring/RecurringPage';
import RecurringDetailPage from '@/pages/recurring/RecurringDetailPage';
import ServersPage from '@/pages/servers/ServersPage';
import ServerDetailPage from '@/pages/servers/ServerDetailPage';

function App() {
  return (
    <BrowserRouter basename={(window as unknown as Record<string, string>).basePath || '/'}>
      <Routes>
        <Route element={<MainLayout />}>
          <Route index element={<DashboardPage />} />
          <Route path="/jobs/detail/:id" element={<JobDetailPage />} />
          <Route path="/jobs/:state" element={<JobListPage />} />
          <Route path="/messages/detail/:id" element={<MessageDetailPage />} />
          <Route path="/messages/:state" element={<MessagesPage />} />
          <Route path="/messages" element={<MessagesPage />} />
          <Route path="/batches/detail/:id" element={<BatchDetailPage />} />
          <Route path="/batches/:state" element={<BatchesPage />} />
          <Route path="/batches" element={<BatchesPage />} />
          <Route path="/recurring/:id" element={<RecurringDetailPage />} />
          <Route path="/recurring" element={<RecurringPage />} />
          <Route path="/servers/:id" element={<ServerDetailPage />} />
          <Route path="/servers" element={<ServersPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
