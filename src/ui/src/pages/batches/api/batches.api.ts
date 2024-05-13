import axios from "axios";
import toast from "react-hot-toast";

import { API_URL_Mock, DEFAULT_ITEMS_PER_PAGE, DEFAULT_PAGE } from "../../../utils/constants";
import { BatchType } from "./batches.models";

export async function getBatches(type: BatchType, page?: string, pageSize?: string): Promise<any> {
    const data = await axios
        .get(
            `${API_URL_Mock}/batches?page=${Number(page) ?? DEFAULT_PAGE}&pageSize=${
                Number(pageSize) ?? DEFAULT_ITEMS_PER_PAGE
            }&type=${type}`
        )
        .then(res => res.data)
        .catch(error => toast.error(error.message));
    return { data } as any;
}
