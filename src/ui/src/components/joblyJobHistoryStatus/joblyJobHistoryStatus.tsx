import styles from "./joblyJobHistoryStatus.module.scss";

interface IJoblyJobHistoryStatusProps {
    status: "scheduled" | "failed" | "succeeded";
    description: string;
    time: string;
    content: string;
}

function JoblyJobHistoryStatus({ status, description, time, content }: IJoblyJobHistoryStatusProps) {
    return (
        <div className={styles["history-status"]}>
            <div
                className={`
                ${styles["history-status__title"]}
                ${styles[`history-status__title--${status}`]}
            `}
            >
                <h5 className={styles["history-status__status"]}>{status}</h5>
                <p>{description}</p>
                <div className={styles["history-status__time"]}>{time}</div>
            </div>
            <div className={styles["history-status__content"]}>{content}</div>
        </div>
    );
}

export default JoblyJobHistoryStatus;
