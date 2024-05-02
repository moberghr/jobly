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

export async function deleteJob(id: string) {
    axios
        .post(
            `${API_URL_Mock}/delete`,
            { id: id },
            {
                headers: {
                    "Content-Type": "application/json",
                    "x-mock-response-code": 200,
                    "x-mock-response-name": "delete",
                },
            }
        )
        .then(res => toast.success("Job successfully delete"))
        .catch(error => toast.error(error));
}
