import { lazy, Suspense, useEffect, useState } from "react";
import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import Paths, { BatchesRouteSubpaths } from "./utils/paths";
import JobWrapper from "./pages/jobs/JobWrapper";
import BatchesWrapper from "./pages/batches/BatchesWrapper";
import { JobRouteSubpaths } from "./utils/paths";
import { deleteJob, getJobDetails, getNavigationData, ResponseJobs } from "./api";
import CallEveryIntervall from "./hooks/callEveryInterval";
import toast from "react-hot-toast";

const Dashboard = lazy(() => import("./pages/dashboard/Dashboard"));
const Batches = lazy(() => import("./pages/batches/Batches"));
const Jobs = lazy(() => import("./pages/jobs/Jobs"));
const ReccuringJobs = lazy(() => import("./pages/recurringJobs/recurringJobs"));
const Navbar = lazy(() => import("./components/navbar/Navbar"));
const Layout = lazy(() => import("./components/layout/Layout"));

const App: React.FC = () => {
    const { dashboard, jobs, recurringJobs, batches } = Paths;

    return (
        <Router>
            <Navbar />
            <Layout>
                <Routes>
                    <Route path={dashboard} element={<Suspense children={<Dashboard />} />} />
                    <Route path={recurringJobs} element={<Suspense children={<ReccuringJobs />} />} />
                    <Route path={jobs} element={<Suspense children={<JobWrapper />} />}>
                        {JobRouteSubpaths.map(obj => (
                            <Route element={<Jobs />} path={`${jobs}${obj.path}`} />
                        ))}
                    </Route>
                    <Route path={batches} element={<Suspense children={<BatchesWrapper />} />}>
                        {BatchesRouteSubpaths.map(obj => (
                            <Route element={<Batches />} path={`${batches}${obj.path}`} />
                        ))}
                    </Route>
                </Routes>
            </Layout>
        </Router>
    );
};

export default App;
