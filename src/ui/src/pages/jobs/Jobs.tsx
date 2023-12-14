import { useLocation } from "react-router";
import VerticalNavbar from "../../components/verticalNavbar/VerticalNavbar";
import { JobRouteSubpaths } from "../../utils/paths";
const Jobs = () => {
    const { pathname } = useLocation();

    return <div>Current path: {pathname}</div>;
};

export default Jobs;
