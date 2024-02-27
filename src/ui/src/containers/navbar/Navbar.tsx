import Navigation from "react-bootstrap/Navbar";
import Nav from "react-bootstrap/Nav";
import NavItem from "../navItem/NavItem";
import { useLocation } from "react-router-dom";
import Routes from "../../utils/paths";
import styles from "./style.module.scss";

const Navbar = () => {
    const { pathname } = useLocation();
    const { dashboard, jobs, recurringJobs, batches } = Routes;

    return (
        <Navigation expand="md" className={styles.navbar}>
            <Navigation.Brand className={styles["navbar-brand"]}>JOBLY</Navigation.Brand>
            <Nav className="me-auto">
                <NavItem link={dashboard} label="Dashboard" quantity={50} isSelected={dashboard === pathname} />
                <NavItem link={jobs} label="Jobs" quantity={236} isSelected={jobs === pathname} />
                <NavItem
                    link={recurringJobs}
                    label="Recurring jobs"
                    quantity={33}
                    isSelected={recurringJobs === pathname}
                />
                <NavItem link={batches} label="Batches" quantity={2} isSelected={batches === pathname} />
            </Nav>
        </Navigation>
    );
};

export default Navbar;
