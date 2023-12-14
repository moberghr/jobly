import React from "react";
import { Outlet } from "react-router-dom";
import Path, { JobRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../components/verticalNavbar/VerticalNavbar";

const JobWrapper: React.FC = () => {
    const { jobs } = Path;

    return (
        <>
            <VerticalNavbar currentPath={jobs} subpaths={JobRouteSubpaths} />
            <Outlet />
        </>
    );
};

export default JobWrapper;
