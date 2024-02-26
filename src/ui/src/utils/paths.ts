import { IconType } from "react-icons";
import {
    FaLayerGroup,
    FaCalendarAlt,
    FaCheckCircle,
    FaExclamation,
    FaTrash,
    FaClock,
    FaPlay,
    FaCircleNotch,
} from "react-icons/fa";
import { GoPulse } from "react-icons/go";
import FailedJobs from "../pages/jobs/FailedJobs";
import BatchesDetails from "../pages/batches/BatchesDetails";

const Routes = {
    dashboard: "/",
    jobs: "/jobs",
    recurringJobs: "/recurring-jobs",
    batches: "/batches",
};

export interface ISubpath {
    label?: string;
    path: string;
    icon?: IconType;
    iconColor?: string;
    component?: () => JSX.Element;
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
        component: FailedJobs,
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

export const BatchesRouteSubpaths: Array<ISubpath> = [
    {
        label: "Started",
        path: "/started",
        icon: FaPlay,
        iconColor: "black",
    },
    {
        label: "Succeeded",
        path: "/succeeded",
        icon: FaCheckCircle,
        iconColor: "black",
    },
    {
        label: "Completed",
        path: "/completed",
        icon: FaCircleNotch,
        iconColor: "black",
    },
    {
        label: "Awaiting",
        path: "/awaiting",
        icon: FaClock,
        iconColor: "black",
    },
    {
        label: "Deleted",
        path: "/deleted",
        icon: FaTrash,
        iconColor: "black",
    },
    {
        path: "/details",
        component: BatchesDetails,
    },
];

export default Routes;
