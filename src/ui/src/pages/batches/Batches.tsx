import { useParams } from "react-router-dom";

const Batches = () => {
    const { batchType } = useParams();

    return <div>Batch type is {batchType}</div>;
};

export default Batches;
