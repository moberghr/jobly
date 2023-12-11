import React from "react";
import styles from "./joblyTitle.module.scss";

interface IJoblyTitleProps {
    children: React.ReactNode;
}

function JoblyTitle({ children }: IJoblyTitleProps) {
    return (
        <>
            <h1 className={styles["title"]}>{children}</h1>
            <div className={styles["title__line"]} />
        </>
    );
}

export default JoblyTitle;
