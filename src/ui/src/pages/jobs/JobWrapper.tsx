import React, { useEffect } from "react";
import { Outlet, useLocation, useParams } from "react-router-dom";

import Path, { JobRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../containers/verticalNavbar/VerticalNavbar";
import styles from "./jobs.module.scss";
import { JobType } from "./api/jobs.models";
import { getJobs } from "./api/jobs.api";
import { useJobsStore } from "../../store/jobs";

const JobWrapper: React.FC = () => {
    const jobsStore = useJobsStore();
    const { jobs } = Path;
    const location = useLocation();
    const { jobType } = useParams();

    useEffect(() => {
        const queryParams = new URLSearchParams(location.search);
        const page = queryParams.get("page") ?? undefined;
        const pageSize = queryParams.get("items") ?? undefined;

        if (!jobType || !(jobType in JobType)) return;

        const fetchJobs = async () => {
            try {
                const response = await getJobs(jobType as JobType, page, pageSize);
                jobsStore.setData(response.data);
            } catch (error) {
                jobsStore.deleteData();
                console.error("Error fetching data:", error);
            }
        };
        fetchJobs();
    }, [location.search, jobType]);

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
