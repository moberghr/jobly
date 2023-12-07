import React from 'react'
import style from "./style.module.scss"
import { useLocation } from 'react-router-dom'
import VerticalNavbar from '../verticalNavbar/VerticalNavbar'

interface Props {
    children: React.ReactNode
}

const Layout: React.FC<Props> = ({children}) => {
    const {pathname} = useLocation();
    
  return (
    <div className={style.container}>
        {pathname != "/" && pathname != "/recurring-jobs" && <VerticalNavbar />}
        {children}
    </div>
  )
}

export default Layout