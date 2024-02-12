import styles from "./style.module.scss";
import RealtimeGraph from "../../components/Graphs/RealtimeGraph";

const Dashboard: React.FC = () => {
    return (
        <div className={styles.container}>
            <div className={styles["graph-container"]}>
                <RealtimeGraph />
            </div>
        </div>
    );
};

export default Dashboard;
