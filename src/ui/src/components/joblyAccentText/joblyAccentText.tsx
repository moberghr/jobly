import styles from "./joblyAccentText.module.scss";

interface IJoblyAccentText {
    children: React.ReactNode;
}

function JoblyAccentText({ children }: IJoblyAccentText) {
    return <span className={styles["accent-text"]}>{children}</span>;
}
export default JoblyAccentText;
