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
} from "chart.js";
import { RealTimeScale, StreamingPlugin } from "chartjs-plugin-streaming";
import "chartjs-adapter-luxon";
import { useRef } from "react";

Chart.register(CategoryScale, LinearScale, PointElement, LineElement, StreamingPlugin, RealTimeScale, LineController);

interface ILine {
    data?: ChartData<"line">;
    options?: ChartOptions<"line">;
}

const data: ChartData<"line"> = {
    datasets: [
        {
            label: "Dataset 1",
            data: [],
            borderColor: "rgb(255, 99, 132)",
            backgroundColor: "rgba(255, 99, 132, 0.5)",
        },
    ],
};

const options: ChartOptions<"line"> = {
    responsive: true,
    plugins: {
        legend: {
            position: "top" as const,
        },
        title: {
            display: true,
            text: "Chart.js Line Chart",
        },
    },
    scales: {},
};

const Dashboard: React.FC<ILine> = () => {
    const ref = useRef(null);

    return (
        <div className={styles.container}>
            <Line options={options} data={data} fallbackContent={<div>bok</div>} ref={ref} />
        </div>
    );
};

export default Dashboard;
