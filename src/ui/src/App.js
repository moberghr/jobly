import { useState, useEffect } from "react";
import "./App.css";

function App() {
  const [display, setDisplay] = useState(0);

  const switchComponent = () => {
    switch (display) {
      case 1:
        return <Table url="/completed" />;
      case 2:
        return <Table url="/created" />;
      case 3:
        return <Table url="/failed" />;
      case 4:
        return <Table url="/scheduled" />;
      case 5:
        return <Table url="/retry" />;
      case 6:
        return <Table url="/processing" />;
      default:
        return <EventsCount />;
    }
  };
  return (
    <div className="App">
      <nav>
        <button
          className={display === 0 ? "active" : ""}
          onClick={() => {
            setDisplay(0);
          }}
        >
          Display job statistics
        </button>
        <button
          className={display === 1 ? "active" : ""}
          onClick={() => {
            setDisplay(1);
          }}
        >
          Display completed jobs
        </button>
        <button
          className={display === 2 ? "active" : ""}
          onClick={() => {
            setDisplay(2);
          }}
        >
          Display created jobs
        </button>
        <button
          className={display === 3 ? "active" : ""}
          onClick={() => {
            setDisplay(3);
          }}
        >
          Display failed jobs
        </button>
        <button
          className={display === 4 ? "active" : ""}
          onClick={() => {
            setDisplay(4);
          }}
        >
          Display scheduled jobs
        </button>
        <button
          className={display === 5 ? "active" : ""}
          onClick={() => {
            setDisplay(5);
          }}
        >
          Display retried jobs
        </button>
        <button
          className={display === 6 ? "active" : ""}
          onClick={() => {
            setDisplay(6);
          }}
        >
          Display jobs in progress
        </button>
      </nav>
      {switchComponent()}
    </div>
  );
}

function Table(props) {
  const [tableData, setTableData] = useState({
    count: 0,
    pageCount: 0,
    items: {},
  });
  const [currentPage, setCurrentPage] = useState(0);

  useEffect(() => {
    setTimeout(() => {
      fetch(`${props.url}?page=${currentPage}`)
        .then((response) => response.json())
        .then((json) => {
          setTableData({
            count: json.totalCount,
            pageCount: json.pageCount,
            items: json.items,
          });
        });
    }, 100);
  }, [tableData, currentPage]);

  const mapper = {
    0: "Enqueued",
    1: "Awaiting",
    2: "Completed",
    3: "Failed",
    4: "Deleted",
  };

  return (
    <>
      <h3 className="nav-header">{props.url.split("/")[1].toUpperCase()}</h3>
      <table>
        <tr class="header-tr">
          <td>Id</td>
          <td>Message</td>
          <td>Create Time</td>
          <td>Type</td>
          <td>Schedule Time</td>
          <td>Processed Time</td>
          <td>Current State</td>
        </tr>
        {Object.entries(tableData.items).map((item) => (
          <tr>
            <td>{item[1].id}</td>
            <td>{item[1].message}</td>
            <td>{item[1].createTime}</td>
            <td>{item[1].type}</td>
            <td>{item[1].scheduleTime}</td>
            <td>{item[1].processedTime}</td>
            <td>{mapper[item[1].currentState]}</td>
          </tr>
        ))}
      </table>
      <div className="footer">
        <button
          onClick={() => {
            if (currentPage > 0) setCurrentPage(currentPage - 1);
          }}
        >
          <i class="fa fa-arrow-left" aria-hidden="true"></i>
        </button>
        <button>Current page {currentPage + 1}</button>
        <button
          onClick={() => {
            if (tableData.pageCount > currentPage + 1)
              setCurrentPage(currentPage + 1);
          }}
        >
          <i class="fa fa-arrow-right" aria-hidden="true"></i>
        </button>
      </div>
    </>
  );
}

function EventsCount() {
  const [count, setCount] = useState({
    total: 0,
    pending: 0,
    scheduled: 0,
    created: 0,
    completed: 0,
    failed: 0,
  });

  useEffect(() => {
    setTimeout(() => {
      fetch("/status")
        .then((response) => response.json())
        .then((temp) => {
          setCount({
            total: temp.total,
            pending: temp.pending,
            scheduled: temp.scheduled,
            created: temp.created,
            completed: temp.completed,
            failed: temp.failed,
          });
        });
    }, 100);
  });

  return (
    <div className="center-div">
      <h2>Job statuses</h2>
      <li>Total: {count.total}</li>
      <li>Pending: {count.pending}</li>
      <li>Scheduled: {count.scheduled}</li>
      <li>Created: {count.created}</li>
      <li>Completed: {count.completed}</li>
      <li>Failed: {count.failed}</li>
    </div>
  );
}

export default App;
