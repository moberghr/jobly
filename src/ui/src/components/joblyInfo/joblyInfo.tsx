import React from "react";
import styles from "./joblyInfo.module.scss";

interface IJoblyInfoProps {
    children: React.ReactNode;
}

function JoblyInfo({ children }: IJoblyInfoProps) {
    return (
        <div className={styles["info"]}>
            <p className={styles["info__text"]}>{children}</p>
        </div>
    );
}

export default JoblyInfo;
