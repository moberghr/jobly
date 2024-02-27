import { useState } from "react";
import Button from "react-bootstrap/Button";
import JoblyTable from "../../components/joblyTable/joblyTable";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import JoblyStatusText from "../../components/joblyStatusText/joblyStatusText";
import styles from "./recurringJobs.module.scss";

const DUMMY_DATA = {
    data: [
        {
            id: "clean-temp",
            cron: "every hour",
            timeZone: "UTC",
            job: "IMaintenanceService.CleanTempDirectory",
            nextExecution: "in 33 minutes",
            lastExecution: { value: "27 minutes ago", failed: true },
        },
        {
            id: "db-clean-users",
            cron: "At 01:00 AM",
            timeZone: "W. Europe Standard Time",
            job: "IUsersService.DefragmentIndexes",
            nextExecution: "in 7 hours",
            lastExecution: { value: "17 hours ago", failed: false },
        },
    ],
    totalCount: 2,
};

const COLUMN_NAMES = {
    id: "Id",
    cron: "Cron",
    timeZone: "Time zone",
    job: "Jobs",
    nextExecution: "Next execution",
    lastExecution: "Last execution",
};

const RecurringJobs = () => {
    const [selectedRows, setSelectedRows] = useState<(number | string)[]>([]);

    const handleTriggerNow = () => {
        console.log("Executing function in parent with selected rows:", selectedRows);
        // Add your logic here based on the selected rows
    };
    return (
        <div className={styles["recuring-jobs"]}>
            <JoblyTitle>Recurring Jobs</JoblyTitle>
            <div className={styles["recuring-jobs__actions"]}>
                <Button variant="primary-blue" onClick={handleTriggerNow}>
                    Trigger now
                </Button>
                <Button variant="outline-dark" disabled>
                    Remove
                </Button>
            </div>

            <div className={styles["table-container"]}>
                <JoblyTable
                    data={DUMMY_DATA}
                    columnNames={COLUMN_NAMES}
                    specialColumnComponents={{ lastExecution: { component: JoblyStatusText } }}
                    selectable
                    onSelectRows={setSelectedRows}
                />
            </div>
        </div>
    );
};

export default RecurringJobs;
