import { useParams } from "react-router-dom";
import { useState } from "react";

import styles from "./styles.module.scss";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import BatchesTable from "../../components/BatchesTable/BatchesTable";

export interface IBatch {
    id: {
        value: string;
        pathId: string;
        mainRoute: string;
    };
    description: string;
    amount: number;
    finished: number;
    created: string;
}

const Batches = () => {
    const { batchType } = useParams();

    const COLUMNS = {
        id: "Id",
        description: "Description",
        nonFinished: "Non finished",
        progress: "Progress",
        created: "Created",
    };

    const [selectedBatch, setSelectedBatch] = useState<IBatch | null>(null);

    return (
        <div className={styles.container}>
            <JoblyTitle>{`${batchType} batches`}</JoblyTitle>
            <BatchesTable />
        </div>
    );
};

export default Batches;
