import { useLocation } from "react-router";

const Jobs = () => {
    const { pathname } = useLocation();
    return <div>Current path: {pathname}</div>;
};

export default Jobs;
