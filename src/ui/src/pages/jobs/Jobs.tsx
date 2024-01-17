import { useLocation } from "react-router";
import styles from "./jobs.module.scss";
import FailedJobs from "./FailedJobs";

import VerticalNavbar from "../../components/verticalNavbar/VerticalNavbar";
import { JobRouteSubpaths } from "../../utils/paths";
const Jobs = () => {
    const { pathname } = useLocation();
    return (
        <div className={styles["jobs"]}>
            {pathname !== "/jobs/failed" && <div>Current path: {pathname}</div>}
            {pathname === "/jobs/failed" && <FailedJobs />}
        </div>
    );
};

export default Jobs;
