import { lazy, Suspense } from "react";
import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { Toaster } from "react-hot-toast";
import Paths, { BatchesRouteSubpaths } from "./utils/paths";
import JobWrapper from "./pages/jobs/JobWrapper";
import BatchesWrapper from "./pages/batches/BatchesWrapper";
import { JobRouteSubpaths } from "./utils/paths";
import BatchesDetails from "./pages/batches/BatchesDetails";
import DetailsJob from "./pages/jobs/DetailsJob";

const Dashboard = lazy(() => import("./pages/dashboard/Dashboard"));
const Batches = lazy(() => import("./pages/batches/Batches"));
const Jobs = lazy(() => import("./pages/jobs/Jobs"));
const ReccuringJobs = lazy(() => import("./pages/recurringJobs/recurringJobs"));
const Navbar = lazy(() => import("./containers/navbar/Navbar"));
const Layout = lazy(() => import("./components/layout/Layout"));

const App: React.FC = () => {
    const { dashboard, jobs, recurringJobs, batches } = Paths;

    return (
        <Router>
            <Toaster />
            <Navbar />
            <Layout>
                <Routes>
                    <Route path={dashboard} element={<Suspense children={<Dashboard />} />} />
                    <Route path={recurringJobs} element={<Suspense children={<ReccuringJobs />} />} />
                    <Route path={jobs} element={<Suspense children={<JobWrapper />} />}>
                        {JobRouteSubpaths.map(obj => {
                            const Component = obj.component ? obj.component : Jobs;
                            return <Route key={obj.path} element={<Component />} path={`${jobs}${obj.path}`} />;
                        })}
                        <Route element={<DetailsJob />} path={`/jobs/details/:id`} />
                    </Route>
                    <Route path={batches} element={<Suspense children={<BatchesWrapper />} />}>
                        <Route element={<Batches />} path={`/batches/:batchType`} />
                        <Route element={<Suspense children={<BatchesDetails />} />} path={`/batches/details/:id`} />
                    </Route>
                </Routes>
            </Layout>
        </Router>
    );
};

export default App;
