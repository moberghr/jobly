import { useSearchParams } from "react-router-dom";
import Button from "react-bootstrap/Button";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import styles from "./jobs.module.scss";

const DUMMY_DATA = {
    name: "IEmailService.SendCampaignEmail",
    id: "238769-3287zri-uhfkaj-sdoq374",
    code: "using Hangfire.ConsoleSample; ...",
    created: "Created a few seconds ago",
    retryCount: 1,
    currentCulture: "ru-RU",
    currentUICulture: "en-US",
};

const DetailsJob = () => {
    let [searchParams, setSearchParams] = useSearchParams();

    //here we need to fetch data for details of job with id searchParams.get("id")

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
                    <div>CurrentCulture</div>
                    <div>{DUMMY_DATA.currentCulture}</div>
                    <div>CurrentUICulture</div>
                    <div>{DUMMY_DATA.currentUICulture}</div>
                </div>
            </div>
            <h4>History</h4>
            <ul>
                <li>Scheduled card</li>
                <li>Failed card</li>
            </ul>
        </>
    );
};

export default DetailsJob;
