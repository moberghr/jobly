import { useRef, useEffect, useMemo } from "react";
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
import styles from "./styles.module.scss";

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const MOCK_DATA = months.map(month => {
    return {
        x: month,
        y: Math.floor(Math.random() * 10),
    };
});

const HistoryGraph = () => {
    const ref = useRef<HTMLCanvasElement>(null);
    const chartRef = useRef<any>(null);

    useEffect(() => {
        const ctx = ref.current;

        const options: ChartOptions = {
            responsive: true,
            maintainAspectRatio: false,
        };

        const data = {
            labels: [],
            datasets: [
                {
                    label: "# of Jobs",
                    data: MOCK_DATA,
                    borderWidth: 1.5,
                    borderColor: "rgba(245, 95, 33, 0.8)",
                    backgroundColor: "rgba(245, 95, 33, 0.25)",
                    fill: true,
                },
            ],
        };

        if (ctx) {
            Chart.register(LinearScale, CategoryScale, LineController, LineElement, PointElement, Filler, Tooltip);
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

    const Graph = useMemo(() => {
        return <canvas id="realtimeGraph" ref={ref}></canvas>;
    }, []);

    return (
        <>
            <div className={styles["header-container"]}>
                <div>History graph</div>
            </div>
            {Graph}
        </>
    );
};

export default HistoryGraph;
