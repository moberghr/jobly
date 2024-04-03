interface IJoblyExpandedRowProps {
    isRowExpanded: boolean;
    props: {
        [key: string]: any;
    };
    component: (props: any) => JSX.Element;
}

const JoblyExpandedRow = ({ isRowExpanded, props, component }: IJoblyExpandedRowProps) => {
    const ExpandedRowColumn = component;
    if (!isRowExpanded) return <></>;
    return (
        <tr>
            <td colSpan={2}></td>
            <td colSpan={2}>
                <ExpandedRowColumn {...props} />
            </td>
        </tr>
    );
};

export default JoblyExpandedRow;
