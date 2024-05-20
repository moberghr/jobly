import styles from "./joblyException.module.scss";

interface IJoblyException {
    title: string;
    subtitle: string;
    exception: string;
}

function JoblyException({ title, subtitle, exception }: IJoblyException) {
    return (
        <div className={styles["exception"]}>
            <h5>{title}</h5>
            <p>{subtitle}</p>
            <p className={styles["exception__content"]}>{exception}</p>
        </div>
    );
}

export default JoblyException;
