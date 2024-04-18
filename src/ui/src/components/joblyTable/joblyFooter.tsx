import React, { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import Pagination from "react-bootstrap/Pagination";
import Dropdown from "react-bootstrap/Dropdown";
import DropdownButton from "react-bootstrap/DropdownButton";

import { ITEMS_PER_PAGE_OPTIONS, DEFAULT_ITEMS_PER_PAGE, DEFAULT_PAGE } from "../../utils/constants";
import styles from "./joblyFooter.module.scss";

interface IJoblyFooterProps {
    totalCount: number;
    dataLength: number;
    selectedCount: number;
}

const JoblyFooter = ({ totalCount, dataLength, selectedCount }: IJoblyFooterProps) => {
    let [searchParams, setSearchParams] = useSearchParams();
    const [pagination, setPagination] = useState({
        itemsPerPage: DEFAULT_ITEMS_PER_PAGE,
        currentPage: DEFAULT_PAGE,
    });

    const maxPage = Math.ceil(totalCount / pagination.itemsPerPage);

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

    return (
        <div className={styles["jobly-table__footer"]}>
            {dataLength > 0 && (
                <>
                    <p>
                        Selected {selectedCount} of {totalCount}
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
                        {pagination.itemsPerPage * pagination.currentPage + dataLength} of <b>{totalCount}</b>
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
            {dataLength === 0 && <p className={styles["jobly-table__no-data"]}>There is no data.</p>}
        </div>
    );
};

export default JoblyFooter;
