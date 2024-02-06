import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./style/style.scss";
import { Toaster } from "react-hot-toast";

const root = ReactDOM.createRoot(document.getElementById("root") as HTMLElement);
root.render(
    <React.StrictMode>
        <App />
        <Toaster />
    </React.StrictMode>
);
