import { useLocation } from "react-router";
import styles from "./jobs.module.scss";
import FailedJobs from "./FailedJobs";
import DetailsJob from "./DetailsJob";

import VerticalNavbar from "../../components/verticalNavbar/VerticalNavbar";
import { JobRouteSubpaths } from "../../utils/paths";

const Jobs = () => {
    const { pathname } = useLocation();
    return (
        <div className={styles["jobs"]}>
            {pathname === "/jobs/failed" && <FailedJobs />}
            {pathname === "/jobs/details" && <DetailsJob />}
        </div>
    );
};

export default Jobs;
