import { Outlet, useLocation } from "react-router-dom";
import { useEffect, useState } from "react";

import Path, { BatchesRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../containers/verticalNavbar/VerticalNavbar";
import { IGetBatchesResponse, BatchType } from "./api/batches.models";
import { getBatches } from "./api/batches.api";

const BatchesWrapper = () => {
    const [data, setData] = useState<IGetBatchesResponse>({ data: [], totalCount: 0 });
    const location = useLocation();

    useEffect(() => {
        const queryParams = new URLSearchParams(location.search);
        const page = queryParams.get("page") ?? undefined;
        const pageSize = queryParams.get("items") ?? undefined;
        const batchType = BatchType[location.pathname.replace("/batches/", "") as keyof typeof BatchType];

        if (!batchType) return;

        const fetchBatches = async () => {
            try {
                const response = await getBatches(batchType, page, pageSize);
                setData(response.data);
            } catch (error) {
                setData({ data: [], totalCount: 0 });
            }
        };
        fetchBatches();
    }, [location.pathname, location.search]);

    return (
        <>
            <VerticalNavbar currentPath={Path.batches} subpaths={BatchesRouteSubpaths} />
            <Outlet context={[data]} />
        </>
    );
};

export default BatchesWrapper;
