import styles from "./jobly.module.scss";
import { IBatch } from "../../pages/batches/Batches";
import { ProgressBar } from "react-bootstrap";

const JoblySummary: React.FC<IBatch> = ({ id, created, description, amount, finished }) => {
    return (
        <table className={styles.container}>
            <tbody>
                <tr>
                    <td>Id</td>
                    <td>{id.value}</td>
                </tr>
                <tr>
                    <td>Description</td>
                    <td>{description}</td>
                </tr>
                <tr>
                    <td>Progress</td>
                    <td style={{ paddingBottom: "5px" }}>
                        {`${finished}/${amount}`}
                        <ProgressBar now={(finished / amount) * 100} variant="danger" striped animated />
                    </td>
                </tr>
                <tr>
                    <td>Created</td>
                    <td>{created}</td>
                </tr>
            </tbody>
        </table>
    );
};

export default JoblySummary;
