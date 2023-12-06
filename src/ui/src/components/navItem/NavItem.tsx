import Item from "react-bootstrap/NavItem";
import { Link } from "react-router-dom";
import styles from "./style.module.scss";

interface INavItemProps {
    link: string
    label: string
    quantity: number
    isSelected?: boolean
}

const NavItem: React.FC<INavItemProps> = ({link, label, quantity, isSelected}) => {
  return (
    <Item className={styles["nav-item"]}>
        <div className={styles["item-container"]}>
            <Link to={link}>{label.toLocaleUpperCase()}</Link>
            <div className={`${styles.quantity} ${isSelected && styles.selected}`}>
                {quantity.toString()}
            </div>
        </div>
    </Item>
  )
}

export default NavItem