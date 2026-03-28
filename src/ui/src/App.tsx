import { BrowserRouter, Routes, Route } from 'react-router-dom';
import MainLayout from '@/layouts/MainLayout';
import DashboardPage from '@/pages/dashboard/DashboardPage';
import JobListPage from '@/pages/jobs/JobListPage';
import JobDetailPage from '@/pages/jobs/JobDetailPage';
import MessagesPage from '@/pages/messages/MessagesPage';
import MessageDetailPage from '@/pages/messages/MessageDetailPage';
import RecurringPage from '@/pages/recurring/RecurringPage';
import ServersPage from '@/pages/servers/ServersPage';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<MainLayout />}>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/jobs/detail/:id" element={<JobDetailPage />} />
          <Route path="/jobs/:state" element={<JobListPage />} />
          <Route path="/messages" element={<MessagesPage />} />
          <Route path="/messages/:id" element={<MessageDetailPage />} />
          <Route path="/recurring" element={<RecurringPage />} />
          <Route path="/servers" element={<ServersPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
