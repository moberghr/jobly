import axios from "axios";
import toast from "react-hot-toast";
import { IGetNavigationCountResponse } from ".";
import { API_URL_Mock } from "../utils/constants";

export async function getNavigationData(): Promise<IGetNavigationCountResponse | undefined> {
    const data = await axios
        .get(`${API_URL_Mock}/navigationdata`)
        .then(res => res.data)
        .catch(error => toast.error(error));
    return data as IGetNavigationCountResponse;
}
