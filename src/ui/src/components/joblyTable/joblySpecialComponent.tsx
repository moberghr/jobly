import { JoblySpecialComponentType } from "../../utils/types";
import styles from "./joblySpecialComponent.module.scss";

interface IJoblySpecialComponentProps {
    specialColumnComponent: {
        component?: (props: any) => JSX.Element;
        props?: { [key: string]: any };
        type: JoblySpecialComponentType;
    };
    data: any;
    isRowExpanded: boolean;
    rowId: string;
    handleExpand: (event: React.MouseEvent<HTMLElement>, id: string | number) => void;
}

const JoblySpecialComponent = ({
    specialColumnComponent,
    data,
    isRowExpanded,
    rowId,
    handleExpand,
}: IJoblySpecialComponentProps) => {
    const SpecialComponent = specialColumnComponent.component;
    if (SpecialComponent)
        switch (specialColumnComponent.type) {
            case JoblySpecialComponentType.FailedJob: {
                return (
                    <td>
                        <SpecialComponent {...data} {...specialColumnComponent.props} />
                        <div className={styles["jobly-table__exception"]}>
                            An exception occured during performance of the job.{" "}
                            <button
                                className={styles["jobly-table__more-details"]}
                                onClick={e => handleExpand(e, rowId)}
                            >
                                {isRowExpanded ? <>Less details...</> : <>More details...</>}
                            </button>
                        </div>
                    </td>
                );
            }
            case JoblySpecialComponentType.Empty: {
                return <></>;
            }
            case JoblySpecialComponentType.Object: {
                return (
                    <td>
                        <SpecialComponent {...data} {...specialColumnComponent.props} />
                    </td>
                );
            }
            default: {
                return (
                    <td>
                        <SpecialComponent {...specialColumnComponent.props}>{data}</SpecialComponent>
                    </td>
                );
            }
        }
    else return <></>;
};

export default JoblySpecialComponent;
