import { useEffect, useState } from "react";

function CallEveryIntervall(fetchFunction: Promise<any>, intervall: number) {
    const [data, setData] = useState(null);
    const [error, setError] = useState(false);
    const [loading, setIsLoading] = useState(true);

    useEffect(() => {
        const intervalId = setInterval(() => {
            const getData = async () => {
                return await fetchFunction;
            };

            getData()
                .then((newData: any) => {
                    setData(newData);
                })
                .catch((error: any) => {
                    setData(null);
                    setError(true);
                });
            setIsLoading(false);
        }, intervall);

        return () => clearInterval(intervalId);
    }, [intervall, fetchFunction]);

    return { data, error, loading };
}

export default CallEveryIntervall;
