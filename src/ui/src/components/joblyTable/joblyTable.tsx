import React, { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import TableComponent from "react-bootstrap/Table";
import Pagination from "react-bootstrap/Pagination";
import Dropdown from "react-bootstrap/Dropdown";
import DropdownButton from "react-bootstrap/DropdownButton";
import { ITEMS_PER_PAGE_OPTIONS, DEFAULT_ITEMS_PER_PAGE, DEFAULT_PAGE } from "../../utils/constants";
import styles from "./joblyTable.module.scss";
import Form from "react-bootstrap/Form";
import JoblyException from "../joblyException/joblyException";

interface IJoblyTableProps {
    data: {
        data: {
            [key: string]: any;
        }[];
        totalCount: number;
    };
    columnNames: {
        [key: string]: string;
    };
    specialColumnComponents?: {
        [key: string]: { component: (props: any) => JSX.Element; props?: { [key: string]: any } };
    };
    selectable?: boolean;
    onSelectRows?: (selectedRows: (number | string)[]) => void;
    failedJobs?: boolean;
}

const JoblyTable = ({
    data,
    columnNames,
    specialColumnComponents,
    selectable,
    onSelectRows,
    failedJobs = false,
}: IJoblyTableProps) => {
    let [searchParams, setSearchParams] = useSearchParams();
    const [pagination, setPagination] = useState({
        itemsPerPage: DEFAULT_ITEMS_PER_PAGE,
        currentPage: DEFAULT_PAGE,
    });

    const maxPage = Math.ceil(data.totalCount / pagination.itemsPerPage);

    const [selectededIds, setSelectedIds] = useState([] as (string | number)[]);
    const [expandedRowId, setExpandedRowId] = useState(undefined as string | number | undefined);

    const handlePaginationChange = (page: number) => {
        setPagination(prev => ({ ...prev, currentPage: page }));
        setSearchParams(params => {
            params.set("page", page.toString());
            return params;
        });
    };

    const handleItemsNumChange = (items: number) => {
        setPagination(prev => ({ ...prev, itemsPerPage: items }));
        setSearchParams(params => {
            params.set("items", items.toString());
            return params;
        });
    };

    const handleSelectedAllRowsChange = () => {
        if (selectededIds.length !== data.data.length) {
            setSelectedIds(data.data.map(item => item.id));
        } else {
            setSelectedIds([]);
        }
    };

    const handleRowSelectedChange = (id: string | number) => {
        setSelectedIds(prev => (prev.includes(id) ? prev.filter(item => item !== id) : [...prev, id]));
    };

    const handleExpand = (event: React.MouseEvent<HTMLElement>, id: string | number) => {
        event.stopPropagation();
        if (expandedRowId === id) setExpandedRowId(undefined);
        else setExpandedRowId(id);
    };

    useEffect(() => {
        setSearchParams(params => {
            if (!params.get("page")) params.set("page", DEFAULT_PAGE.toString());
            else if (params.get("page") !== DEFAULT_PAGE.toString())
                setPagination(prev => ({ ...prev, currentPage: Number(params.get("page")) }));

            if (!params.get("items")) params.set("items", DEFAULT_ITEMS_PER_PAGE.toString());
            else if (params.get("items") !== DEFAULT_ITEMS_PER_PAGE.toString())
                setPagination(prev => ({ ...prev, itemsPerPage: Number(params.get("items")) }));

            return params;
        });
    }, []);

    useEffect(() => {
        if (onSelectRows) onSelectRows(selectededIds);
    }, [selectededIds]);

    return (
        <>
            <TableComponent hover responsive className={styles["jobly-table"]}>
                <thead>
                    <tr>
                        {selectable && (
                            <th key="select-action">
                                <Form.Check
                                    aria-label="select or deselect all rows"
                                    onChange={handleSelectedAllRowsChange}
                                    checked={selectededIds.length === data.data.length}
                                />
                            </th>
                        )}
                        {Object.values(columnNames).map(name => (
                            <th key={name}>{name}</th>
                        ))}
                    </tr>
                </thead>
                {data.data.length > 0 && (
                    <tbody>
                        {data.data.map((row, index) => (
                            <React.Fragment
                                key={
                                    row.id && (typeof row.id === "string" || typeof row.id === "number")
                                        ? row.id
                                        : index
                                }
                            >
                                <tr
                                    className={expandedRowId === row.id ? styles["jobly-table__expanded-row"] : ""}
                                    key={
                                        row.id && (typeof row.id === "string" || typeof row.id === "number")
                                            ? row.id
                                            : index
                                    }
                                    onClick={() => selectable && handleRowSelectedChange(row.id)}
                                >
                                    {selectable && (
                                        <td key="select-row-action">
                                            <Form.Check
                                                aria-label="select or deselect row"
                                                checked={selectededIds.includes(row.id)}
                                                onChange={() => handleRowSelectedChange(row.id)}
                                                onClick={e => {
                                                    e.stopPropagation();
                                                }}
                                            />
                                        </td>
                                    )}
                                    {Object.keys(columnNames).map(name => {
                                        if (specialColumnComponents && specialColumnComponents[name]) {
                                            const SpecialComponent = specialColumnComponents[name].component;
                                            // this first condition is for expandable row on failed jobs page
                                            if (name === "job" && failedJobs)
                                                return (
                                                    <td key={row[name].value}>
                                                        <SpecialComponent
                                                            {...row[name]}
                                                            {...specialColumnComponents[name].props}
                                                        />
                                                        <div className={styles["jobly-table__exception"]}>
                                                            An exception occured during performance of the job.{" "}
                                                            <button
                                                                className={styles["jobly-table__more-details"]}
                                                                onClick={e => handleExpand(e, row.id)}
                                                            >
                                                                {expandedRowId === row.id ? (
                                                                    <>Less details...</>
                                                                ) : (
                                                                    <>More details...</>
                                                                )}
                                                            </button>
                                                        </div>
                                                    </td>
                                                );
                                            if (typeof row[name] === "object")
                                                return (
                                                    <td key={row[name].value}>
                                                        <SpecialComponent
                                                            {...row[name]}
                                                            {...specialColumnComponents[name].props}
                                                        />
                                                    </td>
                                                );
                                            else
                                                return (
                                                    <td key={row[name]}>
                                                        <SpecialComponent {...specialColumnComponents[name].props}>
                                                            {row[name]}
                                                        </SpecialComponent>
                                                    </td>
                                                );
                                        } else if (name === "jobException") return <></>;
                                        else return <td key={row[name]}>{row[name]}</td>;
                                    })}
                                </tr>
                                {expandedRowId === row.id && (
                                    <tr className={styles["jobly-table__expanded-row__content"]}>
                                        <td colSpan={2}></td>
                                        <td colSpan={2}>
                                            <JoblyException
                                                title={row.jobException.title}
                                                subtitle={row.jobException.subtitle}
                                                exception={row.jobException.exception}
                                            />
                                        </td>
                                    </tr>
                                )}
                            </React.Fragment>
                        ))}
                    </tbody>
                )}
            </TableComponent>

            <div className={styles["jobly-table__footer"]}>
                {data.data.length > 0 && (
                    <>
                        <p>
                            Selected {selectededIds.length} of {data.totalCount}
                        </p>
                        <div className={styles["jobly-table__items-per-page"]}>
                            <p>Items per page </p>
                            <DropdownButton
                                id="dropdown-basic-button"
                                title={pagination.itemsPerPage}
                                size="sm"
                                className={styles["jobly-table__dropdown-menu"]}
                            >
                                {ITEMS_PER_PAGE_OPTIONS.map(num => (
                                    <Dropdown.Item key={num} onClick={() => handleItemsNumChange(num)}>
                                        {num}
                                    </Dropdown.Item>
                                ))}
                            </DropdownButton>
                        </div>

                        <p>
                            {pagination.itemsPerPage * pagination.currentPage}-
                            {pagination.itemsPerPage * pagination.currentPage + data.data.length} of{" "}
                            <b>{data.totalCount}</b>
                        </p>

                        <Pagination size="sm">
                            <Pagination.First
                                disabled={pagination.currentPage === 0}
                                onClick={() => handlePaginationChange(0)}
                            />
                            <Pagination.Prev
                                disabled={pagination.currentPage === 0}
                                onClick={() => handlePaginationChange(pagination.currentPage - 1)}
                            />
                            <Pagination.Item
                                active={pagination.currentPage === 0}
                                onClick={() => handlePaginationChange(0)}
                            >
                                {1}
                            </Pagination.Item>
                            {pagination.currentPage > 1 && <Pagination.Ellipsis />}
                            {pagination.currentPage !== 0 && pagination.currentPage !== maxPage - 1 && (
                                <Pagination.Item active={true}>{pagination.currentPage + 1}</Pagination.Item>
                            )}
                            {pagination.currentPage < maxPage - 2 && <Pagination.Ellipsis />}
                            {maxPage !== 1 && (
                                <Pagination.Item
                                    active={pagination.currentPage === maxPage - 1}
                                    onClick={() => handlePaginationChange(maxPage - 1)}
                                >
                                    {maxPage}
                                </Pagination.Item>
                            )}
                            <Pagination.Next
                                disabled={maxPage - 1 === pagination.currentPage}
                                onClick={() => handlePaginationChange(pagination.currentPage + 1)}
                            />
                            <Pagination.Last
                                disabled={maxPage - 1 === pagination.currentPage}
                                onClick={() => handlePaginationChange(maxPage - 1)}
                            />
                        </Pagination>
                    </>
                )}
                {!data.data ||
                    (data.data.length === 0 && <p className={styles["jobly-table__no-data"]}>There is no data.</p>)}
            </div>
        </>
    );
};

export default JoblyTable;
