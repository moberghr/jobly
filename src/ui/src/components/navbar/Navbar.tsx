import Navigation from "react-bootstrap/Navbar";
import Container from "react-bootstrap/Container";
import Nav from "react-bootstrap/Nav";
import { Link } from "react-router-dom";
import NavItem from "../navItem/NavItem";

const Navbar = () => {
  return (
    <Navigation>
      <Navigation.Brand
        className="px-4"
      >
        JOBLY
      </Navigation.Brand>
      <Nav className="me-auto">
        <NavItem link="/" label="Dashboard" quantity={50} isSelected={true} />
        <NavItem link="/jobs" label="Jobs" quantity={236} />
        <NavItem link="/recurring-jobs" label="Recurring jobs" quantity={33} />
        <NavItem link="/batches" label="Batches" quantity={2} />
      </Nav>
    </Navigation>
  );
};

export default Navbar;
