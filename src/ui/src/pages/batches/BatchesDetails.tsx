import React, { useEffect, useState } from "react";
import { IBatch } from "./Batches";
import JoblySummary from "../../components/joblySummary/JoblySummary";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import { useLocation } from "react-router-dom";
import BatchesTable from "../../components/BatchesTable/BatchesTable";
import styles from "./styles.module.scss";

const mockBatch: IBatch = {
    id: {
        value: "msmi431pe",
        pathId: "msmi431pe",
        mainRoute: "batches",
    },
    description: `Email Campaign ${Math.floor(Math.random() * 100)}`,
    amount: 200,
    finished: 165,
    created: "8 min ago",
};

const BatchesDetails = () => {
    const { pathname } = useLocation();
    const title = pathname.split("/").at(3);

    const [batch, setBatch] = useState<IBatch | null>();

    const getBatch = () => {
        try {
            setBatch(mockBatch);
        } catch (error) {
            if (error) {
                console.log("Error: ", error);
            }
        }
    };

    useEffect(() => {
        getBatch();
    }, []);

    return (
        <div className={styles.container}>
            <JoblyTitle>Batch details - {title}</JoblyTitle>
            {batch && <JoblySummary {...batch} />}
            <BatchesTable />
        </div>
    );
};

export default BatchesDetails;
