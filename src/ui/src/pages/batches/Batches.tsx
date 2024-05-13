import { useLocation } from "react-router";
import { useOutletContext } from "react-router-dom";

import { IGetBatchesResponse } from "./api/batches.models";

const Batches = () => {
    const { pathname } = useLocation();
    const [data] = useOutletContext<[IGetBatchesResponse]>();

    return <div>Current path: {pathname}</div>;
};

export default Batches;
