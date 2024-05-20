import Button from "react-bootstrap/Button";
import { useState } from "react";

import JoblyAccentText from "../../components/joblyAccentText/joblyAccentText";
import JoblyInfo from "../../components/joblyInfo/joblyInfo";
import JoblyTable from "../../components/joblyTable/joblyTable";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import JoblyDetailsLink from "../../components/joblyDetailsLink/joblyDetailsLink";
import { FaRotateRight, FaX } from "react-icons/fa6";
import styles from "./jobs.module.scss";
import { JoblySpecialComponentType } from "../../utils/types";

const DUMMY_DATA = {
    data: [
        {
            id: { value: "ff12345", pathId: "ff12345", mainRoute: "jobs" },
            failed: "a minute ago",
            job: { value: "IEmailService.SendCampaignEmail", pathId: "ff12345", mainRoute: "jobs" },
            jobException: {
                title: "System.Net.Mail.SmtpException",
                subtitle: "Syntex error, command unrecognized.",
                exception: "some exception in line 3",
            },
        },
        {
            id: { value: "ffabcd123", pathId: "ffabcd123", mainRoute: "jobs" },
            failed: "a minute ago",
            job: { value: "IEmailService.SendCampaignEmail", pathId: "ffabcd123", mainRoute: "jobs" },
            jobException: {
                title: "System.Net.Mail.SmtpException",
                subtitle: "Syntex error, command unrecognized.",
                exception: "some exception in line 4",
            },
        },
    ],
    totalCount: 2,
};

const COLUMN_NAMES = {
    id: "Id",
    failed: "Failed",
    job: "Job",
};

const FailedJobs = () => {
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
                data={DUMMY_DATA}
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
