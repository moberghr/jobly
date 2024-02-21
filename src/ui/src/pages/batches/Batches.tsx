import { useLocation } from "react-router";
import JoblyTable from "../../components/joblyTable/joblyTable";
import JoblyStatusText from "../../components/joblyStatusText/joblyStatusText";
import JoblyTitle from "../../components/joblyTitle/joblyTitle";
import JoblyDetailsLink from "../../components/joblyDetailsLink/joblyDetailsLink";

const MOCK_DATA = {
    data: [
        {
            id: {
                value: "msmi431pe",
                pathId: "msmi431pe",
                mainRoute: "batches",
            },
            description: "Email Campaign 330",
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
            description: "Email Campaign 505",
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

const Batches = () => {
    const { pathname } = useLocation();
    return (
        <div>
            <JoblyTitle>Batches</JoblyTitle>
            <JoblyTable
                data={MOCK_DATA}
                columnNames={COLUMNS}
                specialColumnComponents={{
                    id: { component: JoblyDetailsLink },
                }}
            />
        </div>
    );
};

export default Batches;
