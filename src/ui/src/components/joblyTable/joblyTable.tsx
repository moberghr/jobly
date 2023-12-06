import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import TableComponent from "react-bootstrap/Table";
import Pagination from "react-bootstrap/Pagination";
import Dropdown from "react-bootstrap/Dropdown";
import DropdownButton from "react-bootstrap/DropdownButton";
import { ITEMS_PER_PAGE_OPTIONS } from "../../utils/constants";
import styles from "./joblyTable.module.scss";

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
        [key: string]: (props: any) => JSX.Element;
    };
}

const JoblyTable = ({ data, columnNames, specialColumnComponents }: IJoblyTableProps) => {
    let [searchParams, setSearchParams] = useSearchParams();
    const [itemsPerPage, setItemsPerPage] = useState(10);
    const [currentPage, setCurrentPage] = useState(0);

    const maxPage = Math.ceil(data.totalCount / itemsPerPage);

    const deleteSearchParams = () => {
        searchParams.delete("page");
        searchParams.delete("items");
        setSearchParams(searchParams);
    };

    const handlePaginationChange = (page: number) => {
        setCurrentPage(page);
        deleteSearchParams();
    };

    const handleItemsNumChange = (items: number) => {
        setItemsPerPage(items);
        deleteSearchParams();
    };

    useEffect(() => {
        setSearchParams(params => {
            if (!params.get("page")) params.set("page", currentPage.toString());
            if (!params.get("items")) params.set("items", itemsPerPage.toString());

            if (params.get("items") && params.get("items") !== itemsPerPage.toString())
                setItemsPerPage(Number(params.get("items")));

            if (params.get("page") && params.get("page") !== currentPage.toString())
                setCurrentPage(Number(params.get("page")));

            return params;
        });
    }, [currentPage, itemsPerPage, setSearchParams]);

    return (
        <>
            <TableComponent hover responsive className={styles["jobly-table"]}>
                <thead>
                    <tr>
                        {Object.values(columnNames).map(name => (
                            <th key={name}>{name}</th>
                        ))}
                    </tr>
                </thead>
                {data.data.length > 0 && (
                    <tbody>
                        {data.data.map((row, index) => (
                            <tr
                                key={
                                    row.id && (typeof row.id === "string" || typeof row.id === "number")
                                        ? row.id
                                        : index
                                }
                            >
                                {Object.keys(columnNames).map(name => {
                                    if (specialColumnComponents && specialColumnComponents[name]) {
                                        const SpecialComponent = specialColumnComponents[name];
                                        if (typeof row[name] === "object")
                                            return (
                                                <td key={row[name].value}>
                                                    <SpecialComponent {...row[name]} />
                                                </td>
                                            );
                                        else
                                            return (
                                                <td key={row[name]}>
                                                    <SpecialComponent>{row[name]}</SpecialComponent>
                                                </td>
                                            );
                                    } else return <td key={row[name]}>{row[name]}</td>;
                                })}
                            </tr>
                        ))}
                    </tbody>
                )}
            </TableComponent>

            <div className={styles["jobly-table__footer"]}>
                {data.data.length > 0 && (
                    <>
                        <p>Selected 0 of {data.totalCount}</p>
                        <div className={styles["jobly-table__items-per-page"]}>
                            <p>Items per page </p>
                            <DropdownButton
                                id="dropdown-basic-button"
                                title={itemsPerPage}
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
                            {itemsPerPage * currentPage}-{itemsPerPage * currentPage + data.data.length} of{" "}
                            <b>{data.totalCount}</b>
                        </p>

                        <Pagination size="sm">
                            <Pagination.First disabled={currentPage === 0} onClick={() => handlePaginationChange(0)} />
                            <Pagination.Prev
                                disabled={currentPage === 0}
                                onClick={() => handlePaginationChange(currentPage - 1)}
                            />
                            <Pagination.Item active={currentPage === 0} onClick={() => handlePaginationChange(0)}>
                                {1}
                            </Pagination.Item>
                            {currentPage > 1 && <Pagination.Ellipsis />}
                            {currentPage !== 0 && currentPage !== maxPage - 1 && (
                                <Pagination.Item active={true}>{currentPage + 1}</Pagination.Item>
                            )}
                            {currentPage < maxPage - 2 && <Pagination.Ellipsis />}
                            {maxPage !== 1 && (
                                <Pagination.Item
                                    active={currentPage === maxPage - 1}
                                    onClick={() => handlePaginationChange(maxPage - 1)}
                                >
                                    {maxPage}
                                </Pagination.Item>
                            )}
                            <Pagination.Next
                                disabled={maxPage - 1 === currentPage}
                                onClick={() => handlePaginationChange(currentPage + 1)}
                            />
                            <Pagination.Last
                                disabled={maxPage - 1 === currentPage}
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
