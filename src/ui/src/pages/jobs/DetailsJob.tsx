import { useParams } from "react-router-dom";
import Button from "react-bootstrap/Button";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import styles from "./jobs.module.scss";
import JoblyJobHistoryStatus from "../../components/joblyJobHistoryStatus/joblyJobHistoryStatus";

const DUMMY_DATA = {
    name: "IEmailService.SendCampaignEmail",
    id: "238769-3287zri-uhfkaj-sdoq374",
    code: "using Hangfire.ConsoleSample; ...",
    created: "Created a few seconds ago",
    retryCount: 1,
};

const DUMMY_HISTORY = [
    {
        status: "scheduled" as "scheduled" | "failed" | "succeeded",
        description: "Retry attempt 1 of 10: Syntax error, command unrecognized.",
        time: "+<1ms",
        content: "Enqueue at: in a few seconds",
    },
    {
        status: "failed" as "scheduled" | "failed" | "succeeded",
        description: "Description...",
        time: "+<1ms",
        content: "Some content...",
    },
    {
        status: "succeeded" as "scheduled" | "failed" | "succeeded",
        description: "Description...",
        time: "+<1ms",
        content: "Some content...",
    },
];

const DetailsJob = () => {
    let { id } = useParams();
    // use this id to fetch correct data

    return (
        <>
            <JoblyTitle>{DUMMY_DATA.name}</JoblyTitle>
            <div className={styles["details__id-and-actions"]}>
                <div className={styles["details__id"]}>Job ID: {DUMMY_DATA.id}</div>
                <div className={styles["details__actions"]}>
                    <Button variant="outline-dark" size="sm">
                        Requeue
                    </Button>
                    <Button variant="secondary" size="sm">
                        Delete
                    </Button>
                </div>
            </div>
            <div className={styles["details__code-info"]}>
                <div className={styles["details__code-info__code"]}>
                    <div className={styles["details__code-info__code__created"]}>{DUMMY_DATA.created}</div>
                    <div>code</div>
                </div>
                <div className={styles["details__code-info__info"]}>
                    <div>RetryCount</div>
                    <div>{DUMMY_DATA.retryCount}</div>
                </div>
            </div>
            <h4>History</h4>
            <div className={styles["details__history"]}>
                {DUMMY_HISTORY.map((history, index) => (
                    <JoblyJobHistoryStatus key={index} {...history} />
                ))}
            </div>
        </>
    );
};

export default DetailsJob;
