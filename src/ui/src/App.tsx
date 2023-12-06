import { lazy, Suspense } from "react";
import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import Paths, { JobsAndBatchesRoutesByType } from "./utils/paths";

const Dashboard = lazy(() => import("./pages/dashboard/Dashboard"));
const Batches = lazy(() => import("./pages/batches/Batches"));
const Jobs = lazy(() => import("./pages/jobs/Jobs"));
const ReccuringJobs = lazy(() => import("./pages/recurring_jobs/RecurringJobs"));
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
					<Route path={jobs} element={<Suspense children={<Jobs />} />} />
					<Route path={batches} element={<Suspense children={<Batches />} />} />

					{JobsAndBatchesRoutesByType.map(obj => {
						if (obj.mainRoute === jobs) {
							return <Route path={obj.path} element={<Jobs />} />;
						} else if (obj.mainRoute === batches) {
							return <Route path={obj.path} element={<Batches />} />;
						}
					})}
				</Routes>
			</Layout>
		</Router>
	);
};

export default App;
