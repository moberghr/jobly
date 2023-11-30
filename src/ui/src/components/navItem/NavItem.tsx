import Item from "react-bootstrap/NavItem";
import { Link } from "react-router-dom";

interface Props {
    link: string
    label: string
    quantity: number
    isSelected?: boolean
}

const NavItem: React.FC<Props> = ({link, label, quantity, isSelected}) => {
  return (
    <Item className="px-3">
        <div className="nav-item-container">
            <Link to={link}>{label.toLocaleUpperCase()}</Link>
            <div className={`quantity ${isSelected && "selected"}`}>
                {quantity.toString()}
            </div>
        </div>
    </Item>
  )
}

export default NavItem