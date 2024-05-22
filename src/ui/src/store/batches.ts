import { create } from "zustand";
import { IGetBatchesResponse } from "../pages/batches/api/batches.models";

const DefaultBatchesState = { data: [], totalCount: 0 };

interface BatchesState {
    data: IGetBatchesResponse;
    setData: (data: IGetBatchesResponse) => void;
    deleteData: () => void;
}

export const useBatchesStore = create<BatchesState>()(set => ({
    data: DefaultBatchesState,
    setData: data => set(() => ({ data: data })),
    deleteData: () => set(() => ({ data: DefaultBatchesState })),
}));
