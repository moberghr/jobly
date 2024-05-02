import React, { useEffect, useState } from "react";
import { Outlet, useLocation } from "react-router-dom";

import Path, { JobRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../containers/verticalNavbar/VerticalNavbar";
import styles from "./jobs.module.scss";
import { IGetJobsResponse, JobType } from "./api/jobs.models";
import { getJobs } from "./api/jobs.api";

const JobWrapper: React.FC = () => {
    const { jobs } = Path;
    const [data, setData] = useState<IGetJobsResponse>({ data: [], totalCount: 0 });
    const location = useLocation();

    useEffect(() => {
        const queryParams = new URLSearchParams(location.search);
        const page = queryParams.get("page") ?? undefined;
        const pageSize = queryParams.get("items") ?? undefined;
        const jobType = JobType[location.pathname.replace("/jobs/", "") as keyof typeof JobType];

        if (!jobType) return;

        const fetchJobs = async () => {
            try {
                const response = await getJobs(jobType, page, pageSize);
                setData(response.data);
            } catch (error) {
                setData({ data: [], totalCount: 0 });
                console.error("Error fetching data:", error);
            }
        };
        fetchJobs();
    }, [location.pathname, location.search]);

    return (
        <>
            <VerticalNavbar currentPath={jobs} subpaths={JobRouteSubpaths} />
            <div className={styles["jobs"]}>
                <Outlet context={[data]} />
            </div>
        </>
    );
};

export default JobWrapper;
