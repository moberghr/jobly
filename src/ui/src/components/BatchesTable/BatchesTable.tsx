import React, { useEffect, useState } from "react";
import styles from "./styles.module.scss";
import JoblyTable from "../joblyTable/joblyTable";
import JoblyDetailsLink from "../joblyDetailsLink/joblyDetailsLink";

enum Status {
    created = "created",
    pending = "pending",
    processing = "processing",
    succeeded = "succeeded",
    finished = "finished",
}

const DUMMY_DATA = {
    data: [
        {
            status: Status.pending,
            id: { value: "ff12345", pathId: "ff12345", mainRoute: "jobs" },
            created: "a minute ago",
            job: { value: "IEmailService.SendCampaignEmail", pathId: "ff12345", mainRoute: "jobs" },
        },
        {
            status: Status.pending,
            id: { value: "ffabcd123", pathId: "ffabcd123", mainRoute: "jobs" },
            created: "a minute ago",
            job: { value: "IEmailService.SendCampaignEmail", pathId: "ffabcd123", mainRoute: "jobs" },
        },
        {
            status: Status.finished,
            id: { value: "c99912", pathId: "c99912", mainRoute: "jobs" },
            created: "hour ago",
            job: { value: "IJobService.SendJob", pathId: "c99912", mainRoute: "jobs" },
        },
        {
            status: Status.created,
            id: { value: "hsadnn23", pathId: "hsadnn23", mainRoute: "jobs" },
            created: "a minute ago",
            job: { value: "IEmailService.SendCampaignEmail", pathId: "ffabcd123", mainRoute: "jobs" },
        },
    ],
    totalCount: 4,
};

const BatchesTable = () => {
    const [status, setStatus] = useState<string>(Status.pending);
    const [jobs, setJobs] = useState(DUMMY_DATA);

    const handleStatusClick = (val: string) => {
        setStatus(val);
    };

    const filterData = () => {
        const filteredData = DUMMY_DATA.data.filter(item => {
            return item.status === status;
        });

        setJobs({ data: filteredData, totalCount: filteredData.length });
    };

    useEffect(() => {
        filterData();
    }, [status]);

    const COLUMN_NAMES = {
        id: "Id",
        status: "State",
        job: "Job",
        created: "created",
    };

    return (
        <div className={styles.container}>
            <div className={styles.navigation}>
                {(Object.keys(Status) as Array<keyof typeof Status>).map(itemStatus => {
                    return (
                        <div
                            key={itemStatus}
                            onClick={() => handleStatusClick(itemStatus)}
                            className={`${styles[`nav-item`]} ${status === itemStatus && styles.active}`}
                        >
                            {itemStatus}

                            <div className={styles.rectangle}></div>
                        </div>
                    );
                })}
            </div>
            <div className={styles["table-container"]}>
                <JoblyTable
                    data={jobs}
                    columnNames={COLUMN_NAMES}
                    specialColumnComponents={{
                        id: { component: JoblyDetailsLink, props: { type: "primary" } },
                        job: { component: JoblyDetailsLink, props: { type: "secondary" } },
                    }}
                />
            </div>
        </div>
    );
};

export default BatchesTable;
