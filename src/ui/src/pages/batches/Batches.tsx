import { useLocation } from "react-router";
import JoblyTable from "../../components/joblyTable/joblyTable";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import JoblyDetailsLink from "../../components/joblyDetailsLink/joblyDetailsLink";
import { useState } from "react";
import styles from "./styles.module.scss";
import { JoblySpecialComponentType } from "../../utils/types";
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
    const MOCK_DATA = {
        data: [
            {
                id: {
                    type: "primary",
                    value: "msmi431pe",
                    pathId: "msmi431pe",
                    mainRoute: "batches",
                },
                description: `Email Campaign ${Math.floor(Math.random() * 100)}`,
                nonFinished: "100/200",
                progress: "50%",
                created: "8 min ago",
            },
            {
                id: {
                    value: "a7234daa2",
                    pathId: "a7234daa2",
                    mainRoute: "batches",
                },
                description: `Email Campaign ${Math.floor(Math.random() * 100)}`,
                nonFinished: "133/512",
                progress: "31%",
                created: "5 hours ago",
            },
        ],
        totalCount: 2,
    };

    const COLUMNS = {
        id: "Id",
        description: "Description",
        nonFinished: "Non finished",
        progress: "Progress",
        created: "Created",
    };
    const { pathname } = useLocation();
    const status = pathname.split("/").at(2);

    const [selectedBatch, setSelectedBatch] = useState<IBatch | null>(null);

    return (
        <div className={styles.container}>
            <JoblyTitle>{`${status} batches`}</JoblyTitle>
            <BatchesTable />
        </div>
    );
};

export default Batches;
