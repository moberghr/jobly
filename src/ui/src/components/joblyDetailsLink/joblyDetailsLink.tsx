import { Link } from "react-router-dom";
import styles from "./joblyDetailsLink.module.scss";

interface IJoblyDetailsLink {
    value: string | number;
    pathId: string;
}

function JoblyDetailsLink({ value, pathId }: IJoblyDetailsLink) {
    return (
        <Link to={"/jobs/details?id=" + pathId} className={styles["details-link"]}>
            {value}
        </Link>
    );
}
export default JoblyDetailsLink;
