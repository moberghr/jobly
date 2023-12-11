import styles from "./joblyStatusText.module.scss";

interface IJoblyStatusTextProps {
    value?: number | string;
    failed?: boolean;
}

function JoblyStatusText({ value, failed }: IJoblyStatusTextProps) {
    return (
        <p
            className={`
                ${styles["status-text"]}
                ${failed ? styles["status-text--failed"] : styles["status-text--ok"]}
            `}
        >
            {value}
        </p>
    );
}
export default JoblyStatusText;
