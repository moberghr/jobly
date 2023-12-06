import style from "./style.module.scss";
import NavItem from "../verticalNavItem/NavItem";
import { FaLayerGroup, FaCalendarAlt, FaCheckCircle, FaExclamation, FaTrash, FaClock } from "react-icons/fa";
import { GoPulse } from "react-icons/go";
import { RouteTypes } from "../../utils/paths";

interface IVerticalNavbar {
	path: string;
}

const VerticalNavbar: React.FC<IVerticalNavbar> = ({ path }) => {
	const pathsArr = path.split("/");
	const type = pathsArr[pathsArr.length - 1];
	const mainRoute = pathsArr[1];

	console.log("Main route: ", mainRoute, "Type: ", type);
	return (
		<div className={style.container}>
			<NavItem
				label="Enqueued"
				link={`/${mainRoute}/${RouteTypes.enqueued}`}
				Icon={FaLayerGroup}
				iconColor={"darkblue"}
				quantity={5}
				isSelected={type === RouteTypes.enqueued}
			/>
			<NavItem
				label="Scheduled"
				link={`/${mainRoute}/${RouteTypes.scheduled}`}
				Icon={FaCalendarAlt}
				iconColor={"darkblue"}
				quantity={215}
				isSelected={type === RouteTypes.scheduled}
			/>
			<NavItem
				label="Processing"
				link={`/${mainRoute}/${RouteTypes.processing}`}
				Icon={GoPulse}
				iconColor={"darkblue"}
				quantity={44125}
				isSelected={type === RouteTypes.processing}
			/>
			<NavItem
				label="Succeeded"
				link={`/${mainRoute}/${RouteTypes.succeeded}`}
				Icon={FaCheckCircle}
				iconColor={"darkblue"}
				quantity={522}
				isSelected={type === RouteTypes.succeeded}
			/>
			<NavItem
				label="Failed"
				link={`/${mainRoute}/${RouteTypes.failed}`}
				Icon={FaExclamation}
				iconColor={"darkblue"}
				quantity={421}
				isSelected={type === RouteTypes.failed}
			/>
			<NavItem
				label="Deleted"
				link={`/${mainRoute}/${RouteTypes.deleted}`}
				Icon={FaTrash}
				iconColor={"darkblue"}
				quantity={2}
				isSelected={type === RouteTypes.deleted}
			/>
			<NavItem
				label="Awaiting"
				link={`/${mainRoute}/${RouteTypes.awaiting}`}
				Icon={FaClock}
				iconColor={"darkblue"}
				quantity={5}
				isSelected={type === RouteTypes.awaiting}
			/>
		</div>
	);
};

export default VerticalNavbar;
