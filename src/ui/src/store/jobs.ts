import { create } from "zustand";
import { IGetJobsResponse } from "../pages/jobs/api/jobs.models";

const DefaultJobsState = { data: [], totalCount: 0 };

interface JobsState {
    data: IGetJobsResponse;
    setData: (data: IGetJobsResponse) => void;
    deleteData: () => void;
}

export const useJobsStore = create<JobsState>()(set => ({
    data: DefaultJobsState,
    setData: data => set(() => ({ data: data })),
    deleteData: () => set(() => ({ data: DefaultJobsState })),
}));
