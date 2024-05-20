import { lazy, Suspense } from "react";
import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { Toaster } from "react-hot-toast";
import Paths, { BatchesRouteSubpaths } from "./utils/paths";
import JobWrapper from "./pages/jobs/JobWrapper";
import BatchesWrapper from "./pages/batches/BatchesWrapper";
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
        <>
            <Toaster />
            <Router>
                <Navbar />
                <Layout>
                    <Routes>
                        <Route path={dashboard} element={<Suspense children={<Dashboard />} />} />
                        <Route path={recurringJobs} element={<Suspense children={<ReccuringJobs />} />} />
                        <Route path={jobs} element={<Suspense children={<JobWrapper />} />}>
                            <Route element={<Jobs />} path={`/jobs/:jobType`} />
                            <Route element={<DetailsJob />} path={`/jobs/details/:id`} />
                        </Route>
                        <Route path={batches} element={<Suspense children={<BatchesWrapper />} />}>
                            {BatchesRouteSubpaths.map(obj => (
                                <Route key={obj.path} element={<Batches />} path={`${batches}${obj.path}`} />
                            ))}
                        </Route>
                    </Routes>
                </Layout>
            </Router>
        </>
    );
};

export default App;
