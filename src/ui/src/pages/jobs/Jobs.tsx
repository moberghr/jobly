import { useLocation } from "react-router";
import VerticalNavbar from "../../components/verticalNavbar/VerticalNavbar";
import { JobRouteSubpaths } from "../../utils/paths";
const Jobs = () => {
    const { pathname } = useLocation();

    return (
        <>
            <VerticalNavbar currentPath={pathname} subpaths={JobRouteSubpaths} />
            <div>Current path: {pathname}</div>
        </>
    );
};

export default Jobs;
