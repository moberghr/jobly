import { Link } from "react-router-dom";
import styles from "./joblyDetailsLink.module.scss";

interface IJoblyDetailsLink {
    value: string | number;
    pathId: string;
    type: "primary" | "secondary";
}

function JoblyDetailsLink({ value, pathId, type }: IJoblyDetailsLink) {
    return (
        <Link
            to={"/jobs/details/" + pathId}
            className={`
                ${styles["details-link"]}
                ${styles["details-link--" + type]}`}
            onClick={e => e.stopPropagation()}
        >
            {value}
        </Link>
    );
}
export default JoblyDetailsLink;
