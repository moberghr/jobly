import styles from "./style.module.scss";
import {
    ChartData,
    ChartOptions,
    Chart,
    CategoryScale,
    LinearScale,
    PointElement,
    LineElement,
    LineController,
    ChartTypeRegistry,
    BubbleDataPoint,
    Point,
    Filler,
    Tooltip,
} from "chart.js";
import { StreamingPlugin, RealTimeScale } from "chartjs-plugin-streaming";
import "chartjs-adapter-luxon";
import { ForwardedRef, forwardRef, useEffect, useRef, useState } from "react";

interface ILine {
    data?: ChartData<"line">;
    options?: ChartOptions<"line">;
}

interface IJoblyObj {
    time: string;
    quantity: number;
}

const Canvas = forwardRef((props, ref: ForwardedRef<HTMLCanvasElement>) => {
    return <canvas id="realtimeGraph" ref={ref}></canvas>;
});

const Dashboard: React.FC<ILine> = () => {
    // const pauseRef = useRef<HTMLButtonElement>(null);
    const isPaused = useRef<boolean>(false);
    const ref = useRef<HTMLCanvasElement>(null);
    const chartRef = useRef<any>(null);

    const togglePause = () => {
        console.log("Pause toggled: ", isPaused.current);
        isPaused.current = !isPaused.current;
    };

    const getData = async (): Promise<IJoblyObj> => {
        try {
            const res = await fetch("http://localhost:6090/jobs");
            if (res.ok) {
                const data = await res.json();
                return data as IJoblyObj;
            }

            return {} as IJoblyObj;
        } catch (error) {
            return {} as IJoblyObj;
        }
    };

    const onRefresh = (
        chart: Chart<keyof ChartTypeRegistry, (number | Point | [number, number] | BubbleDataPoint | null)[], unknown>
    ) => {
        if (!isPaused.current) {
            chart.update("active");
            chartRef.current.options.plugins.streaming.pause = false;

            const data: IJoblyObj = {} as IJoblyObj;

            getData()
                .then(val => {
                    data.quantity = val.quantity;
                    data.time = val.time;
                })
                .then(() => {
                    const date = new Date(data.time);
                    console.log("Date: ", date);
                    const utc = Date.UTC(
                        date.getUTCFullYear(),
                        date.getUTCMonth(),
                        date.getUTCDate(),
                        date.getUTCHours(),
                        date.getUTCMinutes(),
                        date.getUTCSeconds()
                    );

                    chart.data.datasets.map(set => {
                        set.data.push({
                            x: utc,
                            y: data.quantity,
                        });
                    });
                });
        }
        if (isPaused.current) {
            chartRef.current.options.plugins.streaming.pause = true;
            chart.update("none");
        }
    };

    useEffect(() => {
        const ctx = ref.current;

        const options: ChartOptions = {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                x: {
                    type: "realtime",
                    realtime: {
                        duration: 10000,
                        delay: 5000,
                        refresh: 4000,
                        onRefresh,
                    },
                },
            },
        };

        const data = {
            labels: [],
            datasets: [
                {
                    label: "# of Votes",
                    data: [],
                    borderWidth: 1,
                    borderColor: "red",
                    backgroundColor: "whitesmoke",
                    fill: true,
                },
            ],
        };

        if (ctx) {
            Chart.register(
                LinearScale,
                CategoryScale,
                LineController,
                LineElement,
                PointElement,
                RealTimeScale,
                StreamingPlugin,
                Filler,
                Tooltip
            );
            const chart = new Chart(ctx, {
                type: "line",
                data,
                options,
            });

            chartRef.current = chart;

            return () => {
                console.log("Return inside useEffect !");
                console.log("Chart: ", chart);

                chart.destroy(); // removes chart
                ref.current?.remove(); // removes canvas
            };
        }
    }, []);

    return (
        <div className={styles.container}>
            <div className={styles["graph-container"]}>
                <div className={styles["header-container"]}>
                    <div>Realtime graph</div>
                    <button type="button" onClick={togglePause}>
                        Play/Pause
                    </button>
                    <div className={styles["status-container"]}>
                        <div>{isPaused.current ? "PAUSED" : "STREAMING"}</div>
                        <div className={styles["dot"]}></div>
                    </div>
                </div>
                <Canvas ref={ref} />
            </div>
        </div>
    );
};

export default Dashboard;
