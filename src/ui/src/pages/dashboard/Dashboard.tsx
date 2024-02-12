import styles from "./style.module.scss";
import RealtimeGraph from "../../components/Graphs/RealtimeGraph";
import HistoryGraph from "../../components/Graphs/HistoryGraph";

const Dashboard: React.FC = () => {
    return (
        <div className={styles.container}>
            <div className={styles["graph-container"]}>
                <RealtimeGraph />
            </div>
            <div className={styles["graph-container"]}>
                <HistoryGraph />
            </div>
        </div>
    );
};

export default Dashboard;
