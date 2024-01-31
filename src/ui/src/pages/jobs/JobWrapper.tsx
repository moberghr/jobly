import React from "react";
import { Outlet } from "react-router-dom";
import Path, { JobRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../components/verticalNavbar/VerticalNavbar";
import styles from "./jobs.module.scss";

const JobWrapper: React.FC = () => {
    const { jobs } = Path;

    return (
        <>
            <VerticalNavbar currentPath={jobs} subpaths={JobRouteSubpaths} />
            <div className={styles["jobs"]}>
                <Outlet />
            </div>
        </>
    );
};

export default JobWrapper;
