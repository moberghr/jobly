export interface IGetJobDetailsReq {
    id: string;
}

export interface IGetJobDetailsRes {
    name: string;
    id: string;
    code: string;
    created: string;
    retryCount: number;
    history: {
        status: "scheduled" | "failed" | "succeeded";
        description: string;
        time: string;
        content: string;
    }[];
}
