import { useEffect, useState } from "react";

function CallEveryIntervall(fetchFunction: Promise<any>, intervall: number) {
    const [state, setState] = useState({ data: null, error: false, loading: true });

    useEffect(() => {
        setInterval(() => {
            setState(state => ({ data: state.data, error: false, loading: true }));

            const getData = async () => {
                return await fetchFunction;
            };
            getData();

            getData()
                .then((newData: any) => setState({ data: newData, error: false, loading: false }))
                .catch((error: any) => {
                    console.log(error);
                    setState({ data: null, error: true, loading: false });
                });
        }, intervall);
    }, [intervall, fetchFunction]);
    useEffect(() => () => console.log("unmount"), []);
    return state;
}

export default CallEveryIntervall;
