import React from "react";
import style from "./style.module.scss";
import { useLocation } from "react-router-dom";

interface ILayout {
    children: React.ReactNode;
}

const Layout: React.FC<ILayout> = ({ children }) => {
    const { pathname } = useLocation();

    return <div className={style.container}>{children}</div>;
};

export default Layout;
