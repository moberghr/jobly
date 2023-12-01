import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import Dashboard from "./pages/dashboard/index";
import Batches from "./pages/batches/index";
import Jobs from "./pages/jobs/index";
import ReccuringJobs from "./pages/recurring_jobs/index";
import Navbar from "./components/navbar/Navbar";
import Layout from "./components/layout/Layout";
const App: React.FC = () => {
  return (
    <Router>
		<Navbar />
    <Layout>
      <Routes>
        <Route path="/" element={<Dashboard />} />
        <Route path="/recurring-jobs" element={<ReccuringJobs />} />
        <Route path="/batches" element={<Batches />} />
        <Route path="/jobs" element={<Jobs />} />
      </Routes>
    </Layout>
 
    </Router>
  );
};

export default App;
