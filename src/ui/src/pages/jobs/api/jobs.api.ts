import axios from "axios";
import toast from "react-hot-toast";

import { API_URL_Mock, DEFAULT_ITEMS_PER_PAGE, DEFAULT_PAGE } from "../../../utils/constants";
import { JobType } from "./jobs.models";

export async function getJobs(type: JobType, page?: string, pageSize?: string): Promise<any> {
    const data = await axios
        .get(
            `${API_URL_Mock}/jobs?page=${Number(page) ?? DEFAULT_PAGE}&pageSize=${
                Number(pageSize) ?? DEFAULT_ITEMS_PER_PAGE
            }&type=${type}`
        )
        .then(res => res.data)
        .catch(error => toast.error(error));
    return { data } as any;
}
