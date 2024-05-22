import { useParams } from "react-router-dom";
import { JobRouteSubpaths } from "../../utils/paths";

const Jobs = () => {
    const { jobType } = useParams();

    let Component = JobRouteSubpaths.find(job => job.path === "/" + jobType)?.component;
    if (!Component) Component = () => <div>Page not found</div>;

    return <Component />;
};

export default Jobs;
