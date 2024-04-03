import styles from "./joblyExpandedRow.module.scss";

interface IJoblyExpandedRowProps {
    isRowExpanded: boolean;
    props: {
        [key: string]: any;
    };
    component: (props: any) => JSX.Element;
}

const JoblyExpandedRow = ({ isRowExpanded, props, component }: IJoblyExpandedRowProps) => {
    const ExpandedRowColumn = component;
    return isRowExpanded ? (
        <tr className={styles["jobly-table__expanded-row__content"]}>
            <td colSpan={2}></td>
            <td colSpan={2}>
                <ExpandedRowColumn {...props} />
            </td>
        </tr>
    ) : (
        <></>
    );
};

export default JoblyExpandedRow;
