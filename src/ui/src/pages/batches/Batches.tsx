import { useLocation } from "react-router";

const Batches = () => {
	const { pathname } = useLocation();

	return <div>Current path: {pathname}</div>;
};

export default Batches;
