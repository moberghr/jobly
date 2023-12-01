import style from "./style.module.scss"
import NavItem from "../verticalNavItem/NavItem";
import { FaLayerGroup, FaCalendarAlt, FaCheckCircle, FaExclamation, FaTrash, FaClock } from "react-icons/fa";
import { GoPulse } from "react-icons/go";

const VerticalNavbar = () => {
  return (
    <div className={style.container}>
      <NavItem label="Enqueued" Icon={FaLayerGroup} quantity={5}/>
      <NavItem label="Scheduled" Icon={FaCalendarAlt} quantity={215}/>
      <NavItem label="Processing" Icon={GoPulse} quantity={44125}/>
      <NavItem label="Succeeded" Icon={FaCheckCircle} quantity={522}/>
      <NavItem label="Failed" Icon={FaExclamation} quantity={421}/>
      <NavItem label="Deleted" Icon={FaTrash} quantity={2}/>
      <NavItem label="Awaiting" Icon={FaClock} quantity={5}/>
    </div>
  )
}

export default VerticalNavbar