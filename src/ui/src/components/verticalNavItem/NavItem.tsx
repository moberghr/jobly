import React from "react";
import { IconType } from "react-icons";
import style from "./style.module.scss";
import { useNavigate } from "react-router";

interface IVerticalNavItemProps {
	Icon: IconType;
	iconColor?: string;
	label: string;
	link: string;
	quantity: number;
	isSelected?: boolean;
}

const NavItem: React.FC<IVerticalNavItemProps> = ({ Icon, label, link, quantity, iconColor, isSelected }) => {
	const navigate = useNavigate();

	return (
		<div
			className={isSelected ? `${style.container} ${style.selected}` : style.container}
			onClick={() => navigate(link)}
		>
			<Icon className={style.icon} color={iconColor} />
			<div className={style.label}>{label}</div>
			<div className={style.quantity}>{quantity.toString()}</div>
		</div>
	);
};

export default NavItem;
