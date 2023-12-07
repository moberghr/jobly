import React from 'react'
import { IconType } from 'react-icons'
import style from "./style.module.scss"

interface IVerticalNavItemProps {
    Icon: IconType,
    iconColor?: string,
    label: string,
    quantity: number
}
const NavItem: React.FC<IVerticalNavItemProps> = ({Icon, label, quantity, iconColor}) => {
  return (
    <div className={style.container}>
        <Icon className={style.icon} color={iconColor} />
        <div className={style.label}>{label}</div>
        <div className={style.quantity}>{quantity.toString()}</div>
    </div>
  )
}

export default NavItem