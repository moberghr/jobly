import style from "./style.module.scss";
import NavItem from "../verticalNavItem/NavItem";
import { ISubpath } from "../../utils/paths";
import { useLocation } from "react-router-dom";

interface IVerticalNavbar {
    currentPath: string;
    subpaths: Array<ISubpath>;
}

const VerticalNavbar: React.FC<IVerticalNavbar> = ({ currentPath, subpaths }) => {
    const { pathname } = useLocation();

    return (
        <div className={style.container}>
            {subpaths.map(
                obj =>
                    obj.label &&
                    obj.path &&
                    obj.icon && (
                        <NavItem
                            key={obj.label}
                            label={obj.label}
                            link={`${currentPath}${obj.path}`}
                            Icon={obj.icon}
                            iconColor={obj.iconColor}
                            quantity={Math.floor(Math.random() * (1000 - 1) + 1)}
                            isSelected={`${currentPath}${obj.path}` === pathname}
                        />
                    )
            )}
        </div>
    );
};

export default VerticalNavbar;
