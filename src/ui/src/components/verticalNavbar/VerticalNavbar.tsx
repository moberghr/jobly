import style from "./style.module.scss";
import NavItem from "../verticalNavItem/NavItem";
import { ISubpath } from "../../utils/paths";

interface IVerticalNavbar {
    currentPath: string;
    subpaths: Array<ISubpath>;
}

const VerticalNavbar: React.FC<IVerticalNavbar> = ({ currentPath, subpaths }) => {
    const pathsArr = currentPath.split("/");
    const currentSubpath = `/${pathsArr[pathsArr.length - 1]}`;
    const mainRoute = `/${pathsArr[1]}`;

    return (
        <div className={style.container}>
            {subpaths.map(obj => (
                <NavItem
                    label={obj.label}
                    link={`${mainRoute}${obj.path}`}
                    Icon={obj.icon}
                    iconColor={obj.iconColor}
                    quantity={Math.floor(Math.random() * (1000 - 1) + 1)}
                    isSelected={currentSubpath === obj.path}
                />
            ))}
        </div>
    );
};

export default VerticalNavbar;
