import Button from "react-bootstrap/Button";
import JoblyAccentText from "../../components/joblyAccentText/joblyAccentText";
import JoblyInfo from "../../components/joblyInfo/joblyInfo";
import JoblyTable from "../../components/joblyTable/joblyTable";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import JoblyDetailsLink from "../../components/joblyDetailsLink/joblyDetailsLink";
import { FaRotateRight, FaX } from "react-icons/fa6";
import styles from "./jobs.module.scss";

const DUMMY_DATA = {
    data: [
        {
            id: { value: "ff12345", pathId: "ff12345" },
            failed: "a minute ago",
            job: "IEmailService.SendCampaignEmail",
        },
        {
            id: { value: "ffabcd123", pathId: "ffabcd123" },
            failed: "a minute ago",
            job: "IEmailService.SendCampaignEmail",
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
                <Button variant="primary-blue">
                    <FaRotateRight />
                    Requeue jobs
                </Button>
                <Button variant="outline-dark">
                    <FaX />
                    Delete selected
                </Button>
            </div>
            <JoblyTable
                data={DUMMY_DATA}
                columnNames={COLUMN_NAMES}
                specialColumnComponents={{ id: JoblyDetailsLink }}
            />
        </>
    );
};

export default FailedJobs;
