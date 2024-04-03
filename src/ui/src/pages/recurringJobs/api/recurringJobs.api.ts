import axios from "axios";
import { IGetRecurringJobsTableResponse } from "./recurringJobs.models";
import toast from "react-hot-toast";
import { API_URL_Mock } from "../../../utils/constants";

export async function getRecurringJobTableData(
    page: number,
    pageSize: number
): Promise<IGetRecurringJobsTableResponse> {
    const data = await axios
        .get(`${API_URL_Mock}/tableData?page=${page}&pageSize=${pageSize}`)
        .then(res => res.data)
        .catch(error => toast.error(error));
    return { data: data, totalCount: data.length } as IGetRecurringJobsTableResponse;
}
