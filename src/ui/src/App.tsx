import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import Dashboard from "./pages/dashboard/index";
import Batches from "./pages/batches/index";
import Jobs from "./pages/jobs/index";
import ReccuringJobs from "./pages/recurring_jobs/index";
import Navbar from "./components/navbar/Navbar";
import { ReturnedJobs, getNavigationData } from "./api";
import { useEffect, useState } from "react";

const App: React.FC = () => {
	const [navigationData, setNavigationData] = useState({} as ReturnedJobs);

	const setJobsData = async () => {
		const data = await getNavigationData();
		setNavigationData(data);
	};

	console.log(navigationData);

	useEffect(() => {
		setJobsData();
	}, []);
	return (
		<Router>
			<Navbar />
			<Routes>
				<Route path="/" element={<Dashboard />} />
				<Route path="/batches" element={<Batches />} />
				<Route path="/jobs" element={<Jobs />} />
				<Route path="/recurring-jobs" element={<ReccuringJobs />} />
			</Routes>
		</Router>
	);
};

export default App;
