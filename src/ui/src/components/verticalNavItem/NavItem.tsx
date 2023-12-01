import React from 'react'
import { IconType } from 'react-icons'
import style from "./style.module.scss"

interface Props {
    Icon: IconType
    label: string,
    quantity: number
}
const NavItem: React.FC<Props> = ({Icon, label, quantity}) => {
  return (
    <div className={style.container}>
        <Icon className={style.icon} />
        <div className={style.label}>{label}</div>
        <div className={style.quantity}>{quantity.toString()}</div>
    </div>
  )
}

export default NavItem