import React from "react";
import { Routes, Route } from "react-router-dom";
import Path, { JobRouteSubpaths } from "../../utils/paths";
import Jobs from "../../pages/jobs/Jobs";

const JobWrapper: React.FC = () => {
    return (
        <Routes>
            {JobRouteSubpaths.map(subpath => (
                <Route element={<Jobs />} path={`${Path.jobs}${subpath.path}`} />
            ))}
        </Routes>
    );
};

export default JobWrapper;
