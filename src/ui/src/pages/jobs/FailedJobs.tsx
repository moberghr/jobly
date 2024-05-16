import Button from "react-bootstrap/Button";
import { useState } from "react";
import { FaRotateRight, FaX } from "react-icons/fa6";

import JoblyAccentText from "../../components/joblyAccentText/joblyAccentText";
import JoblyInfo from "../../components/joblyInfo/joblyInfo";
import JoblyTable from "../../components/joblyTable/joblyTable";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import JoblyDetailsLink from "../../components/joblyDetailsLink/joblyDetailsLink";
import styles from "./jobs.module.scss";
import { JoblySpecialComponentType } from "../../utils/types";
import { useJobsStore } from "../../store/jobs";

const COLUMN_NAMES = {
    id: "Id",
    failed: "Failed",
    job: "Job",
};

const FailedJobs = () => {
    const { data } = useJobsStore();
    const [selectedRows, setSelectedRows] = useState<(number | string)[]>([]);

    const handleRequeueJobs = () => {
        console.log("Executing function in parent with selected rows:", selectedRows);
        // Add your logic here based on the selected rows
    };

    const handleDeleteJobs = () => {
        console.log("Executing function in parent with selected rows:", selectedRows);
        // Add your logic here based on the selected rows
    };

    return (
        <>
            <JoblyTitle>Failed Jobs</JoblyTitle>
            <JoblyInfo>
                <b>Failed jobs do not become expired</b> to allow you to re-queue them without any time pressure. You
                should re-queue or delete them manually, or apply{" "}
                <JoblyAccentText>AutomaticRetry(OnAttemptsExceeded = AttemptsExceededAction.Delete)</JoblyAccentText>{" "}
                attribute to delete them automatically.
            </JoblyInfo>
            <div className={styles["jobs__button-actions"]}>
                <Button variant="primary-blue" onClick={handleRequeueJobs}>
                    <FaRotateRight />
                    Requeue jobs
                </Button>
                <Button variant="outline-dark" onClick={handleDeleteJobs}>
                    <FaX />
                    Delete selected
                </Button>
            </div>
            <JoblyTable
                data={data}
                columnNames={COLUMN_NAMES}
                specialColumnComponents={{
                    id: {
                        component: JoblyDetailsLink,
                        props: { type: "primary" },
                        type: JoblySpecialComponentType.Object,
                    },
                    job: {
                        component: JoblyDetailsLink,
                        props: { type: "secondary" },
                        type: JoblySpecialComponentType.FailedJob,
                    },
                    jobException: {
                        type: JoblySpecialComponentType.Empty,
                    },
                }}
                selectable
                onSelectRows={setSelectedRows}
            />
        </>
    );
};

export default FailedJobs;
