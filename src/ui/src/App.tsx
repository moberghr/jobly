import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import Dashboard from "./pages/dashboard/index";
import Batches from "./pages/batches/index";
import Jobs from "./pages/jobs/index";
import ReccuringJobs from "./pages/recurring_jobs/index";
import Navbar from "./components/navbar/Navbar";
import { ResponseJobs, getNavigationData } from "./api";
import { useEffect, useState } from "react";

const App: React.FC = () => {
	const [navigationData, setNavigationData] = useState({} as ResponseJobs | undefined);

	const getData = async () => {
		const navData = await getNavigationData();
		setNavigationData(navData);
	};

	useEffect(() => {
		getData();
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
