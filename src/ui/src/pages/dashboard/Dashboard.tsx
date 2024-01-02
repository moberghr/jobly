import styles from "./style.module.scss";
import { Line } from "react-chartjs-2";
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
import { RealTimeScale, StreamingPlugin } from "chartjs-plugin-streaming";
import "chartjs-adapter-luxon";
import { useRef } from "react";

Chart.register(
    CategoryScale,
    LinearScale,
    PointElement,
    LineElement,
    StreamingPlugin,
    RealTimeScale,
    LineController,
    Filler,
    Tooltip
);

interface ILine {
    data?: ChartData<"line">;
    options?: ChartOptions<"line">;
}

const data: ChartData<"line"> = {
    datasets: [
        {
            data: [],
            label: "Dataset 1",
            fill: true,
            borderColor: "grey",
            backgroundColor: "rgba(48, 244, 115, 0.7)",
        },
    ],
};

const onRefresh = (
    chart: Chart<keyof ChartTypeRegistry, (number | Point | [number, number] | BubbleDataPoint | null)[], unknown>
) => {
    chart.data.datasets.map(set => {
        set.data.push({
            x: Date.now(),
            y: Math.floor(Math.random() * 10),
        });
    });
};

const options: ChartOptions<"line"> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {},
    scales: {
        x: {
            type: "realtime",
            realtime: {
                duration: 20000,
                refresh: 1000,
                delay: 1000,
                onRefresh,
            },
        },
    },
};

const labels = ["1", "2", "3", "4", "5", "6"];

const data2: ChartData<"line"> = {
    labels,
    datasets: [
        {
            data: [
                { x: 2, y: 3 },
                { x: 6, y: 6 },
                { x: 2, y: 3 },
                { x: 6, y: 6 },
                { x: 0, y: 0 },
            ],
            label: "Dataset 1",
            fill: true,
            tension: 0.2,
        },
    ],
};

const options2: ChartOptions<"line"> = {
    responsive: true,
    plugins: {},
};

const Dashboard: React.FC<ILine> = () => {
    const ref = useRef();

    return (
        <div className={styles.container}>
            <div className={styles["graph-container"]}>
                <div>Realtime graph</div>
                <Line options={options} data={data} ref={ref} className={styles.graph} />
            </div>
            <div className={styles["graph-container"]}>
                <div>History graph</div>
                <Line options={options2} data={data2} />
            </div>
        </div>
    );
};

export default Dashboard;
