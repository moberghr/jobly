import axios from "axios";
import { API_URL_Mock } from "../../../utils/constants";
import toast from "react-hot-toast";

export async function getJobDetails(id: string): Promise<any> {
    const data = await axios
        .get(`${API_URL_Mock}/job/${id}`)
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
