import Button from 'react-bootstrap/Button';

import Table from '../../components/table';
import Title from '../../components/title';

const Index = () => {
  return (
    <div className='content-container'>
      <Title>Recurring Jobs</Title>
      <Button variant='primary-blue' disabled>
        Trigger now
      </Button>
      <Button variant='outline-dark' disabled>
        Remove
      </Button>
      <Table />
    </div>
  );
};

export default Index;
