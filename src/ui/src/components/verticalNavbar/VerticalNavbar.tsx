import style from "./style.module.scss"
import NavItem from "../verticalNavItem/NavItem";
import { FaLayerGroup, FaCalendarAlt, FaCheckCircle, FaExclamation, FaTrash, FaClock } from "react-icons/fa";
import { GoPulse } from "react-icons/go";

const VerticalNavbar = () => {
  return (
    <div className={style.container}>
      <NavItem label="Enqueued" Icon={FaLayerGroup} iconColor={"darkblue"} quantity={5}/>
      <NavItem label="Scheduled" Icon={FaCalendarAlt} iconColor={"darkblue"} quantity={215}/>
      <NavItem label="Processing" Icon={GoPulse} iconColor={"darkblue"} quantity={44125}/>
      <NavItem label="Succeeded" Icon={FaCheckCircle} iconColor={"darkblue"} quantity={522}/>
      <NavItem label="Failed" Icon={FaExclamation} iconColor={"darkblue"} quantity={421}/>
      <NavItem label="Deleted" Icon={FaTrash} iconColor={"darkblue"} quantity={2}/>
      <NavItem label="Awaiting" Icon={FaClock} iconColor={"darkblue"} quantity={5}/>
    </div>
  )
}

export default VerticalNavbar