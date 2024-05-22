import { Outlet, useLocation, useParams } from "react-router-dom";
import { useEffect } from "react";

import { useBatchesStore } from "../../store/batches";
import Path, { BatchesRouteSubpaths } from "../../utils/paths";
import VerticalNavbar from "../../containers/verticalNavbar/VerticalNavbar";
import { BatchType } from "./api/batches.models";
import { getBatches } from "./api/batches.api";

const BatchesWrapper = () => {
    const location = useLocation();
    const batchesStore = useBatchesStore();
    const { batchType } = useParams();

    useEffect(() => {
        const queryParams = new URLSearchParams(location.search);
        const page = queryParams.get("page") ?? undefined;
        const pageSize = queryParams.get("items") ?? undefined;

        if (!batchType || !(batchType in BatchType)) return;

        const fetchBatches = async () => {
            try {
                const response = await getBatches(batchType as BatchType, page, pageSize);
                batchesStore.setData(response.data);
            } catch (error) {
                batchesStore.deleteData();
                console.error("Error fetching data:", error);
            }
        };
        fetchBatches();
    }, [location.search, batchType]);

    return (
        <>
            <VerticalNavbar currentPath={Path.batches} subpaths={BatchesRouteSubpaths} />
            <Outlet />
        </>
    );
};

export default BatchesWrapper;
