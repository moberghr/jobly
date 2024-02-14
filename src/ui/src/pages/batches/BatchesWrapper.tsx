import { Outlet } from "react-router-dom";
import Path, { BatchesRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../containers/verticalNavbar/VerticalNavbar";

const BatchesWrapper = () => {
    return (
        <>
            <VerticalNavbar currentPath={Path.batches} subpaths={BatchesRouteSubpaths} />
            <Outlet />
        </>
    );
};

export default BatchesWrapper;
