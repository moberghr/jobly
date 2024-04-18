import React, { useEffect, useState } from "react";
import TableComponent from "react-bootstrap/Table";
import Form from "react-bootstrap/Form";

import styles from "./joblyTable.module.scss";
import JoblyFooter from "./joblyFooter";
import JoblySpecialComponent from "./joblySpecialComponent";
import { JoblySpecialComponentType } from "../../utils/types";
import JoblyExpandedRow from "./joblyExpandedRow";
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
        [key: string]: {
            component?: (props: any) => JSX.Element;
            props?: { [key: string]: any };
            type: JoblySpecialComponentType;
        };
    };
    selectable?: boolean;
    onSelectRows?: (selectedRows: (number | string)[]) => void;
}

const JoblyTable = ({ data, columnNames, specialColumnComponents, selectable, onSelectRows }: IJoblyTableProps) => {
    const [selectededIds, setSelectedIds] = useState([] as (string | number)[]);
    const [expandedRowId, setExpandedRowId] = useState(undefined as string | number | undefined);

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
                        {data.data.map((row, index) => {
                            const key =
                                row.id && (typeof row.id === "string" || typeof row.id === "number") ? row.id : index;
                            const isRowExpanded = expandedRowId === row.id;
                            return (
                                <React.Fragment key={key}>
                                    <tr
                                        className={isRowExpanded && styles["jobly-table__expanded-row"]}
                                        key={key}
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
                                        {Object.keys(columnNames).map((name, i) => {
                                            if (specialColumnComponents && specialColumnComponents[name]) {
                                                return (
                                                    <JoblySpecialComponent
                                                        key={row[name] + i}
                                                        specialColumnComponent={specialColumnComponents[name]}
                                                        data={row[name]}
                                                        isRowExpanded={isRowExpanded}
                                                        rowId={row.id}
                                                        handleExpand={handleExpand}
                                                    />
                                                );
                                            } else return <td key={row[name]}>{row[name]}</td>;
                                        })}
                                    </tr>
                                    <JoblyExpandedRow
                                        isRowExpanded={isRowExpanded}
                                        props={row.jobException}
                                        component={JoblyException}
                                    />
                                </React.Fragment>
                            );
                        })}
                    </tbody>
                )}
            </TableComponent>

            <JoblyFooter
                dataLength={data?.data?.length ?? 0}
                totalCount={data.totalCount}
                selectedCount={selectededIds.length}
            />
        </>
    );
};

export default JoblyTable;
