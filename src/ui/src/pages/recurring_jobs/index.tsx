import Button from 'react-bootstrap/Button';

import Table from '../../components/table';
import Title from '../../components/title';
import StatusText from '../../components/statusText';

const DUMMY_DATA = {
  data: [
    {
      id: 'clean-temp',
      cron: 'every hour',
      timeZone: 'UTC',
      job: 'IMaintenanceService.CleanTempDirectory',
      nextExecution: 'in 33 minutes',
      lastExecution: { value: '27 minutes ago', failed: true },
    },
    {
      id: 'db-clean-users',
      cron: 'At 01:00 AM',
      timeZone: 'W. Europe Standard Time',
      job: 'IUsersService.DefragmentIndexes',
      nextExecution: 'in 7 hours',
      lastExecution: { value: '17 hours ago', failed: false },
    },
  ],
  totalCount: 2,
};

const COLUMN_NAMES = {
  id: 'Id',
  cron: 'Cron',
  timeZone: 'Time zone',
  job: 'Jobs',
  nextExecution: 'Next execution',
  lastExecution: 'Last execution',
};

const Index = () => {
  return (
    <div className='content-container'>
      <Title>Recurring Jobs</Title>
      <div className='actions-wrapper'>
        <Button variant='primary-blue' disabled>
          Trigger now
        </Button>
        <Button variant='outline-dark' disabled>
          Remove
        </Button>
      </div>

      <Table
        data={DUMMY_DATA}
        columnNames={COLUMN_NAMES}
        specialColumns={['lastExecution']}
        specialColumnComponents={{ lastExecution: StatusText }}
      />
    </div>
  );
};

export default Index;
