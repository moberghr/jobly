import { IconType } from "react-icons";
import { FaLayerGroup, FaCalendarAlt, FaCheckCircle, FaExclamation, FaTrash, FaClock } from "react-icons/fa";
import { GoPulse } from "react-icons/go";

const Routes = {
    dashboard: "/",
    jobs: "/jobs",
    recurringJobs: "/recurring-jobs",
    batches: "/batches",
};

export interface ISubpath {
    label: string;
    path: string;
    icon: IconType;
    iconColor: string;
}

export const JobRouteSubpaths: Array<ISubpath> = [
    {
        label: "Enqueued",
        path: "/enqueued",
        icon: FaLayerGroup,
        iconColor: "black",
    },
    {
        label: "Scheduled",
        path: "/scheduled",
        icon: FaCalendarAlt,
        iconColor: "black",
    },
    {
        label: "Processing",
        path: "/processing",
        icon: GoPulse,
        iconColor: "black",
    },
    {
        label: "Succeeded",
        path: "/succeeded",
        icon: FaCheckCircle,
        iconColor: "black",
    },
    {
        label: "Failed",
        path: "/failed",
        icon: FaExclamation,
        iconColor: "black",
    },
    {
        label: "Deleted",
        path: "/deleted",
        icon: FaTrash,
        iconColor: "black",
    },
    {
        label: "Awaiting",
        path: "/awaiting",
        icon: FaClock,
        iconColor: "black",
    },
];

export default Routes;
